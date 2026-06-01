import json
import secrets
from pathlib import Path

from fastapi import APIRouter, Depends, HTTPException, Response
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.config import settings
from app.database import get_db
from app.dependencies import get_current_team, require_admin
from app.models import Match, MatchParticipant, Submission, Team
from app.schemas import LiveMatchStateOut, LivePlayerStateOut, MatchOut, PlayerMapEntry, PlayerMapOut, TriggerMatchRequest
from app.score_utils import serialize_score

router = APIRouter()


def _serialize_match(match: Match) -> MatchOut:
    return MatchOut(
        id=match.id,
        mode=match.mode,
        submission_a_id=match.submission_a_id,
        submission_b_id=match.submission_b_id,
        status=match.status,
        score_a=serialize_score(match.score_a),
        score_b=serialize_score(match.score_b),
        scheduled_at=match.scheduled_at,
        finished_at=match.finished_at,
    )


async def _select_current_or_latest_match(db: AsyncSession) -> Match | None:
    live_result = await db.execute(
        select(Match)
        .where(Match.status.in_(["pending", "running"]))
        .order_by(Match.scheduled_at.desc(), Match.id.desc())
        .limit(1)
    )
    match = live_result.scalar_one_or_none()
    if match is not None:
        return match

    latest_result = await db.execute(
        select(Match).order_by(Match.scheduled_at.desc(), Match.id.desc()).limit(1)
    )
    return latest_result.scalar_one_or_none()


def _load_json_document(path: Path) -> dict:
    try:
        with path.open(encoding="utf-8") as f:
            payload = json.load(f)
    except (OSError, json.JSONDecodeError):
        return {}

    return payload if isinstance(payload, dict) else {}


def _load_live_player_stats() -> tuple[dict[str, dict], dict]:
    payload = _load_json_document(Path(settings.live_server_data_dir) / "player-stats.json")
    players = payload.get("players")
    if not isinstance(players, list):
        return {}, payload

    by_token: dict[str, dict] = {}
    for player in players:
        if not isinstance(player, dict):
            continue
        token = str(player.get("token") or "").strip()
        if not token:
            continue
        by_token[token] = player
    return by_token, payload


def _load_live_result_scores() -> dict[str, int]:
    payload = _load_json_document(Path(settings.live_server_data_dir) / "result.json")
    scores = payload.get("scores")
    if not isinstance(scores, dict):
        return {}

    parsed: dict[str, int] = {}
    for token, score in scores.items():
        try:
            parsed[str(token)] = int(score)
        except (TypeError, ValueError):
            continue
    return parsed


@router.get("/", response_model=list[MatchOut])
async def list_my_matches(
    team: Team = Depends(get_current_team),
    db: AsyncSession = Depends(get_db),
):
    sub_ids_result = await db.execute(
        select(Submission.id).where(Submission.team_id == team.id)
    )
    sub_ids = [r[0] for r in sub_ids_result.all()]
    if not sub_ids:
        return []

    participant_match_result = await db.execute(
        select(MatchParticipant.match_id).where(MatchParticipant.submission_id.in_(sub_ids))
    )
    participant_match_ids = [r[0] for r in participant_match_result.all()]

    result = await db.execute(
        select(Match)
        .where(
            (Match.submission_a_id.in_(sub_ids))
            | (Match.submission_b_id.in_(sub_ids))
            | (Match.id.in_(participant_match_ids))
        )
        .order_by(Match.scheduled_at.desc())
        .limit(50)
    )
    return [_serialize_match(match) for match in result.scalars().all()]


@router.post("/admin/trigger", response_model=MatchOut, dependencies=[Depends(require_admin)])
async def trigger_match(body: TriggerMatchRequest, db: AsyncSession = Depends(get_db)):
    # A match needs two distinct sides; equal ids would build one participant and
    # the live game could never settle as a 2-player match.
    if body.submission_a_id == body.submission_b_id:
        raise HTTPException(status_code=400, detail="两个提交必须不同")

    for sub_id in (body.submission_a_id, body.submission_b_id):
        result = await db.execute(
            select(Submission).where(Submission.id == sub_id, Submission.status == "ready")
        )
        if not result.scalar_one_or_none():
            raise HTTPException(status_code=404, detail=f"Submission {sub_id} not found or not ready")

    match = Match(
        mode="manual",
        submission_a_id=body.submission_a_id,
        submission_b_id=body.submission_b_id,
        status="pending",
    )
    db.add(match)
    await db.flush()
    sub_result = await db.execute(
        select(Submission).where(Submission.id.in_([body.submission_a_id, body.submission_b_id]))
    )
    submissions = list(sub_result.scalars().all())
    # player_token is the live-server auth credential — must be an unguessable
    # secret, never derivable from public ids (see evaluator/main.py arena_loop).
    db.add_all([
        MatchParticipant(
            match_id=match.id,
            submission_id=sub.id,
            team_id=sub.team_id,
            player_token=secrets.token_urlsafe(24),
        )
        for sub in submissions
    ])
    await db.commit()
    await db.refresh(match)
    return _serialize_match(match)


@router.get("/admin/all", response_model=list[MatchOut], dependencies=[Depends(require_admin)])
async def list_all_matches(db: AsyncSession = Depends(get_db)):
    result = await db.execute(
        select(Match).order_by(Match.scheduled_at.desc()).limit(100)
    )
    return [_serialize_match(match) for match in result.scalars().all()]


@router.get("/current/player-map", response_model=PlayerMapOut)
async def current_player_map(response: Response, db: AsyncSession = Depends(get_db)):
    """Public read-only mapping from in-game player_id → team name for the match
    currently being played (or, if none is running, the most recently scheduled
    one). Spectators use this to label scoreboard entries with team names instead
    of "选手 N".

    Deliberately exposes only the non-secret player_id, not player_token: the
    token is the live-server auth credential and leaking it would let an outsider
    bind to the live WebSocket as that team's agent. The player_id here is the
    same 0-based index the game server assigns (it loads TOKENS in participant-id
    order and numbers players in that order), so it lines up with the playerId in
    the live GAME_STATE stream the spectator already sees."""
    # Prefer a live match; fall back to the latest scheduled so the spectator
    # UI can still show labels during the brief gap between matches. id.desc()
    # is a deterministic tie-break when several rows share a scheduled_at.
    match = await _select_current_or_latest_match(db)

    # Short cache so crawling the public endpoint can't hammer the DB.
    response.headers["Cache-Control"] = "public, max-age=5"

    if match is None:
        return PlayerMapOut(match_id=None, status=None, players=[])

    rows = await db.execute(
        select(MatchParticipant, Team)
        .join(Team, Team.id == MatchParticipant.team_id)
        .where(MatchParticipant.match_id == match.id)
        .order_by(MatchParticipant.id)
    )
    # The game server assigns playerId in TOKENS order, which the evaluator builds
    # from participants ordered by id — so enumerate in that same order.
    players = [
        PlayerMapEntry(
            player_id=index,
            team_id=participant.team_id,
            team_name=team.name,
        )
        for index, (participant, team) in enumerate(rows.all())
    ]
    return PlayerMapOut(match_id=match.id, status=match.status, players=players)


@router.get("/current/live-state", response_model=LiveMatchStateOut)
async def current_live_state(response: Response, db: AsyncSession = Depends(get_db)):
    """Public live observer snapshot merged from DB participant metadata and the
    live server's exported player-stats/result files.

    Exposes only non-secret public fields; player_token stays server-side and is
    used solely to join the filesystem snapshots back to participant rows."""
    match = await _select_current_or_latest_match(db)
    response.headers["Cache-Control"] = "public, max-age=5"

    if match is None:
        return LiveMatchStateOut(
            match_id=None,
            status=None,
            stage=None,
            current_month=None,
            current_day=None,
            current_tick=None,
            players=[],
        )

    rows = await db.execute(
        select(MatchParticipant, Team, Submission)
        .join(Team, Team.id == MatchParticipant.team_id)
        .join(Submission, Submission.id == MatchParticipant.submission_id, isouter=True)
        .where(MatchParticipant.match_id == match.id)
        .order_by(MatchParticipant.id.asc())
    )
    stats_by_token, stats_doc = _load_live_player_stats()
    result_scores = _load_live_result_scores()

    players: list[LivePlayerStateOut] = []
    for index, (participant, team, submission) in enumerate(rows.all()):
        stats = stats_by_token.get(participant.player_token, {})
        player_name = str(stats.get("playerName") or "").strip() or None
        active_cards = stats.get("activeCards")
        if not isinstance(active_cards, list):
            active_cards = []

        score = stats.get("score")
        if score is None:
            score = result_scores.get(participant.player_token)

        players.append(
            LivePlayerStateOut(
                player_id=index,
                team_id=team.id,
                team_name=team.name,
                submission_id=submission.id if submission is not None else None,
                submission_name=submission.name if submission is not None else None,
                player_name=player_name,
                connected=bool(stats.get("connected", False)),
                in_game=bool(stats.get("inGame", True)),
                score=serialize_score(score),
                current_nav=serialize_score(stats.get("currentNav")),
                mora=serialize_score(stats.get("mora")),
                frozen_mora=serialize_score(stats.get("frozenMora")),
                gold=stats.get("gold"),
                frozen_gold=stats.get("frozenGold"),
                locked_gold=stats.get("lockedGold"),
                monthly_trade_count=stats.get("monthlyTradeCount"),
                active_cards=[str(card) for card in active_cards],
            )
        )

    return LiveMatchStateOut(
        match_id=match.id,
        status=match.status,
        stage=stats_doc.get("stage"),
        current_month=stats_doc.get("currentMonth"),
        current_day=stats_doc.get("currentDay"),
        current_tick=stats_doc.get("currentTick"),
        players=players,
    )
