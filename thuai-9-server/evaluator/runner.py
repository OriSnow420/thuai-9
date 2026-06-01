from __future__ import annotations

from dataclasses import dataclass
import json
import logging
import os
import shutil
import time

from compiler import get_docker
from config import settings

logger = logging.getLogger(__name__)

GAME_TIMEOUT_SECONDS = 360
POLL_INTERVAL = 1
MANAGED_AGENT_LABEL = "thuai9.managed"
MANAGED_AGENT_VALUE = "evaluator-agent"
LIVE_SERVER_OUTPUT_FILES = (
    "result.json",
    "replay.dat",
    "player-stats.json",
    "stat.dat",
)


@dataclass(frozen=True)
class MatchAgent:
    submission_id: int
    team_id: int
    image_ref: str
    token: str
    player_name: str | None = None


def _redact_token(token: str) -> str:
    # player_token is the live-server auth credential; never emit it in clear to
    # logs. Show only a short prefix so operators can still correlate entries.
    if not token:
        return "<empty>"
    return f"{token[:2]}***"


def _spawn_agent(client, match_id: int, agent: MatchAgent):
    if not agent.image_ref:
        raise ValueError(f"submission {agent.submission_id} missing image reference")

    # The agent runs untrusted contestant code. mem/cpu are capped; pids_limit
    # caps the process count so a fork bomb can't evade the memory cap. NOTE for
    # maintainers: this container shares live_server_network to reach the live
    # game server, which also exposes egress / other services on that network —
    # isolating agents onto a dedicated internal-only network with the live
    # server alone is a hardening follow-up, intentionally not done here.
    return client.containers.run(
        image=agent.image_ref,
        environment={
            "TOKEN": agent.token,
            "SERVER": settings.live_server_url,
            "PLAYER_NAME": agent.player_name or "",
        },
        network=settings.live_server_network,
        mem_limit="256m",
        nano_cpus=500_000_000,
        pids_limit=512,
        labels={
            MANAGED_AGENT_LABEL: MANAGED_AGENT_VALUE,
            "thuai9.match_id": str(match_id),
            "thuai9.submission_id": str(agent.submission_id),
        },
        detach=True,
        remove=False,
    )


def _prepare_live_server_config_dir() -> str:
    config_dir = settings.live_server_config_dir
    os.makedirs(config_dir, exist_ok=True)
    config_path = os.path.join(config_dir, "config.json")
    legacy_config_path = os.path.join(settings.match_data_dir, "live-server-config", "config.json")

    if not os.path.isfile(config_path):
        config_payload = {
            "game": {
                # Allow arena matches to start even when only one
                # dispatched ready submission is available.
                "minimumPlayerCount": 1,
            }
        }

        if os.path.isfile(legacy_config_path):
            try:
                with open(legacy_config_path, encoding="utf-8") as f:
                    loaded = json.load(f)
                if isinstance(loaded, dict):
                    config_payload = loaded
            except (OSError, json.JSONDecodeError):
                logger.warning("Failed to load legacy live server config from %s", legacy_config_path)

        with open(config_path, "w", encoding="utf-8") as f:
            json.dump(config_payload, f, ensure_ascii=False)

    return config_dir


def _prepare_live_server_data_dir() -> str:
    data_dir = settings.live_server_data_dir
    os.makedirs(data_dir, exist_ok=True)

    for filename in LIVE_SERVER_OUTPUT_FILES:
        try:
            os.remove(os.path.join(data_dir, filename))
        except FileNotFoundError:
            pass

    return data_dir


def _resolve_host_path(client, container_path: str) -> str:
    container = client.containers.get(settings.evaluator_container_name)
    mounts = container.attrs.get("Mounts", [])
    normalized_path = os.path.normpath(container_path)

    best_source: str | None = None
    best_destination: str | None = None
    best_length = -1

    for mount in mounts:
        destination = mount.get("Destination")
        source = mount.get("Source")
        if not destination or not source:
            continue

        normalized_destination = os.path.normpath(destination)
        if normalized_path != normalized_destination and not normalized_path.startswith(normalized_destination + os.sep):
            continue

        if len(normalized_destination) > best_length:
            best_source = source
            best_destination = normalized_destination
            best_length = len(normalized_destination)

    if best_source is None or best_destination is None:
        raise RuntimeError(f"cannot resolve host path for {container_path}")

    relative_path = os.path.relpath(normalized_path, best_destination)
    if relative_path == ".":
        return best_source
    return os.path.normpath(os.path.join(best_source, relative_path))


def _start_live_server(client, agents: list[MatchAgent], live_server_image: str | None = None):
    if not settings.restart_live_server:
        return None

    try:
        old_server = client.containers.get(settings.live_server_container)
        old_server.remove(force=True)
    except Exception:
        pass

    tokens = [agent.token for agent in agents]
    player_names = {
        agent.token: agent.player_name
        for agent in agents
        if agent.player_name
    }
    logger.info(
        "Starting live game server container %s with %d players using image %s",
        settings.live_server_container,
        len(tokens),
        live_server_image or settings.live_server_image,
    )
    config_dir = _prepare_live_server_config_dir()
    data_dir = _prepare_live_server_data_dir()
    host_config_path = _resolve_host_path(client, os.path.join(config_dir, "config.json"))
    host_data_dir = _resolve_host_path(client, data_dir)
    server = client.containers.run(
        image=live_server_image or settings.live_server_image,
        name=settings.live_server_container,
        entrypoint=["./server"],
        environment={
            "TOKENS": ",".join(tokens),
            "PLAYER_NAMES_JSON": json.dumps(player_names, ensure_ascii=False),
        },
        labels={
            "thuai9.managed": "live-server",
        },
        volumes={
            host_data_dir: {"bind": "/app/data", "mode": "rw"},
            host_config_path: {"bind": "/app/config/config.json", "mode": "ro"},
        },
        network=settings.live_server_network,
        detach=True,
        remove=False,
    )

    deadline = time.time() + 20
    while time.time() < deadline:
        server.reload()
        if server.status == "running":
            return server
        time.sleep(0.5)

    raise RuntimeError(f"live server {settings.live_server_container} did not become running")


def cleanup_runtime_containers() -> None:
    client = get_docker()

    try:
        old_server = client.containers.get(settings.live_server_container)
        old_server.remove(force=True)
        logger.info("Removed stale live game server container %s", settings.live_server_container)
    except Exception:
        pass

    try:
        stale_agents = client.containers.list(
            all=True,
            filters={"label": f"{MANAGED_AGENT_LABEL}={MANAGED_AGENT_VALUE}"},
        )
    except Exception:
        logger.exception("Failed to list stale agent containers")
        return

    for container in stale_agents:
        try:
            container.remove(force=True)
            logger.info("Removed stale agent container %s", container.name)
        except Exception:
            logger.exception("Failed to remove stale agent container %s", container.name)


def _read_result(result_path: str) -> dict[str, int] | None:
    if not os.path.isfile(result_path):
        return None

    try:
        with open(result_path, encoding="utf-8") as f:
            result = json.load(f)
    except (OSError, json.JSONDecodeError):
        return None

    scores = result.get("scores")
    if not isinstance(scores, dict):
        return None

    parsed: dict[str, int] = {}
    for token, score in scores.items():
        try:
            parsed[str(token)] = int(score)
        except (TypeError, ValueError):
            continue
    return parsed


def _collect_agent_logs(containers) -> str:
    snippets: list[str] = []
    for container in containers:
        try:
            logs = container.logs(tail=80).decode("utf-8", errors="replace").strip()
        except Exception as exc:
            logs = f"<failed to read logs: {exc}>"
        if logs:
            snippets.append(f"== {container.name} ==\n{logs}")
    return "\n\n".join(snippets)


# Cap per-agent log to a fixed size before persisting so a runaway agent can't
# explode the database. Matches what the portal will display anyway.
PER_AGENT_LOG_BYTES = 64 * 1024
# Bound how many lines we pull out of Docker into evaluator memory in the first
# place — a runaway agent can emit gigabytes, but we only keep the tail anyway.
LOG_TAIL_LINES = 5000


def _read_container_log(container) -> str:
    try:
        raw = container.logs(stdout=True, stderr=True, tail=LOG_TAIL_LINES)
    except Exception as exc:
        return f"<failed to read logs: {exc}>"
    text = raw.decode("utf-8", errors="replace")
    if len(text) > PER_AGENT_LOG_BYTES:
        truncated = len(text) - PER_AGENT_LOG_BYTES
        text = f"... [truncated {truncated} bytes] ...\n" + text[-PER_AGENT_LOG_BYTES:]
    return text


def run_match(
    match_id: int,
    agents: list[MatchAgent],
    live_server_image: str | None = None,
) -> tuple[dict[int, int] | None, str, dict[int, str]]:
    """Run all ready submissions in one live arena game.

    Returns ``(scores, error_log, agent_logs)`` where ``agent_logs`` maps
    each submission id to the captured stdout+stderr of that agent's
    container. ``agent_logs`` is populated regardless of outcome so the
    portal can show teams their own output even on success.
    """
    if not agents:
        return None, "没有可参赛的 ready 提交", {}

    client = get_docker()
    data_dir = settings.live_server_data_dir
    result_path = os.path.join(data_dir, "result.json")
    os.makedirs(data_dir, exist_ok=True)

    containers = []
    server = None
    # Indexed by submission_id so callers can persist per-team logs even when
    # an exception forces an early return.
    container_by_submission: dict[int, object] = {}
    agent_logs: dict[int, str] = {}

    def _snapshot_logs() -> dict[int, str]:
        return {
            sub_id: _read_container_log(container)
            for sub_id, container in container_by_submission.items()
        }

    try:
        server = _start_live_server(client, agents, live_server_image=live_server_image)

        # Give the WebSocket listener a short moment to bind after start.
        time.sleep(2)

        for agent in agents:
            container = _spawn_agent(client, match_id, agent)
            containers.append(container)
            container_by_submission[agent.submission_id] = container
            logger.info(
                "Spawned agent for match %d: submission=%d team=%d token=%s",
                match_id,
                agent.submission_id,
                agent.team_id,
                _redact_token(agent.token),
            )

        expected_tokens = {agent.token for agent in agents}
        token_to_submission = {agent.token: agent.submission_id for agent in agents}
        deadline = time.time() + GAME_TIMEOUT_SECONDS
        latest_complete_scores: dict[str, int] | None = None

        while time.time() < deadline:
            scores = _read_result(result_path)
            if scores is not None:
                missing = sorted(expected_tokens - scores.keys())
                if not missing:
                    latest_complete_scores = scores

            if server is None:
                if latest_complete_scores is not None:
                    agent_logs = _snapshot_logs()
                    return _scores_by_submission(latest_complete_scores, token_to_submission), "", agent_logs
            else:
                server.reload()
                if server.status in {"exited", "dead"}:
                    agent_logs = _snapshot_logs()
                    if latest_complete_scores is not None:
                        return _scores_by_submission(latest_complete_scores, token_to_submission), "", agent_logs
                    return None, "游戏服已结束，但未写出完整结果", agent_logs
            time.sleep(POLL_INTERVAL)

        latest_scores = _read_result(result_path) or {}
        # Report missing/received results by submission id, never by raw
        # player_token (the token is the live-server auth credential and this
        # error_log is persisted and shown in the portal).
        missing_subs = sorted(
            str(token_to_submission.get(t, "?"))
            for t in (expected_tokens - latest_scores.keys())
        )
        received_subs = {
            token_to_submission.get(t, "?"): score
            for t, score in latest_scores.items()
        }
        log_suffix = _collect_agent_logs(containers)
        agent_logs = _snapshot_logs()
        details = f"比赛超时，缺少结果 submission: {', '.join(missing_subs) if missing_subs else '无'}"
        if received_subs:
            details += f"\n已收到结果: {received_subs}"
        if log_suffix:
            details += f"\n\n{log_suffix}"
        return None, details, agent_logs

    except Exception as exc:
        logger.exception("Match %d failed", match_id)
        log_suffix = _collect_agent_logs(containers)
        agent_logs = _snapshot_logs()
        return None, f"{exc}\n\n{log_suffix}".strip(), agent_logs

    finally:
        if server is not None:
            try:
                server.stop(timeout=5)
            except Exception:
                pass
            try:
                server.remove(force=True)
            except Exception:
                pass

        for container in containers:
            try:
                container.stop(timeout=5)
            except Exception:
                pass
            try:
                container.remove(force=True)
            except Exception:
                pass

        shutil.rmtree(os.path.join(settings.match_data_dir, str(match_id)), ignore_errors=True)


def _scores_by_submission(
    scores: dict[str, int],
    token_to_submission: dict[str, int],
) -> dict[int, int]:
    return {
        token_to_submission[token]: score
        for token, score in scores.items()
        if token in token_to_submission
    }
