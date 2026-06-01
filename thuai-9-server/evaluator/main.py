import asyncio
import logging
import os
import random
import secrets
from datetime import datetime, timedelta, timezone

from sqlalchemy import delete, inspect, select, text, update
from sqlalchemy.ext.asyncio import AsyncSession, create_async_engine
from sqlalchemy.orm import sessionmaker

from compiler import compile_submission
from config import settings
from runner import GAME_TIMEOUT_SECONDS, MatchAgent, cleanup_runtime_containers, run_match

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
logger = logging.getLogger(__name__)

engine = create_async_engine(settings.database_url, echo=False)
AsyncSessionLocal = sessionmaker(engine, class_=AsyncSession, expire_on_commit=False)

# Import models after engine is set up
from sqlalchemy import BigInteger, Boolean, Column, DateTime, ForeignKey, Index, Integer, String, Text, UniqueConstraint, func
from sqlalchemy.orm import DeclarativeBase

# Keep at most this many match-log rows per submission so a long-lived arena
# can't grow the table without bound.
LOG_RETENTION_PER_SUBMISSION = 50


class Base(DeclarativeBase):
    pass


class Team(Base):
    __tablename__ = "teams"
    id = Column(Integer, primary_key=True)
    name = Column(String(64), nullable=False)
    email = Column(String(255), nullable=False)


class Submission(Base):
    __tablename__ = "submissions"
    id = Column(Integer, primary_key=True)
    team_id = Column(Integer, nullable=False)
    name = Column(String(64), nullable=False, default="未命名代码")
    language = Column(String(8), nullable=False, default="docker", server_default="docker")
    status = Column(String(16), nullable=False)
    is_dispatched = Column(Boolean, nullable=False, default=False, server_default=text("false"))
    error_log = Column(Text)
    artifact_path = Column(Text)
    uploaded_at = Column(DateTime(timezone=True))
    compiled_at = Column(DateTime(timezone=True))


class Match(Base):
    __tablename__ = "matches"
    id = Column(Integer, primary_key=True)
    mode = Column(String(8), nullable=False)
    submission_a_id = Column(Integer, ForeignKey("submissions.id"), nullable=False)
    submission_b_id = Column(Integer, ForeignKey("submissions.id"), nullable=False)
    status = Column(String(16), nullable=False)
    score_a = Column(BigInteger)
    score_b = Column(BigInteger)
    error_log = Column(Text)
    scheduled_at = Column(DateTime(timezone=True), default=lambda: datetime.now(timezone.utc))
    finished_at = Column(DateTime(timezone=True))


class MatchParticipant(Base):
    __tablename__ = "match_participants"
    id = Column(Integer, primary_key=True)
    match_id = Column(Integer, ForeignKey("matches.id", ondelete="CASCADE"), nullable=False)
    submission_id = Column(Integer, ForeignKey("submissions.id"), nullable=False)
    team_id = Column(Integer, nullable=False)
    player_token = Column(String(64), nullable=False)
    score = Column(BigInteger)


class SubmissionMatchLog(Base):
    __tablename__ = "submission_match_logs"
    # Mirror of submission-api's app/models.py SubmissionMatchLog. The
    # authoritative DDL (including the teams FK) is owned by submission-api,
    # which starts first (see docker-compose depends_on). team_id has no
    # ForeignKey here because this Base does not model the teams table; if this
    # fallback create_all ever wins the bootstrap, the column is still correct,
    # only the teams cascade is deferred to submission-api's schema.
    __table_args__ = (
        Index("ix_submission_match_logs_submission_created", "submission_id", "created_at"),
    )
    id = Column(Integer, primary_key=True)
    match_id = Column(Integer, ForeignKey("matches.id", ondelete="CASCADE"), nullable=False)
    submission_id = Column(Integer, ForeignKey("submissions.id", ondelete="CASCADE"), nullable=False)
    team_id = Column(Integer, nullable=False)
    log = Column(Text, nullable=False, default="")
    created_at = Column(DateTime(timezone=True), server_default=func.now())


class Competition(Base):
    __tablename__ = "competitions"
    __table_args__ = (
        Index("ix_competitions_status_scheduled", "status", "scheduled_at"),
    )

    id = Column(Integer, primary_key=True)
    name = Column(String(128), nullable=False)
    description = Column(Text)
    status = Column(String(16), nullable=False, default="scheduled", server_default=text("'scheduled'"))
    scheduled_at = Column(DateTime(timezone=True), nullable=False)
    submission_deadline = Column(DateTime(timezone=True))
    live_server_image = Column(Text, nullable=False)
    created_by_email = Column(String(255), nullable=False)
    created_by_name = Column(String(64), nullable=False)
    match_id = Column(Integer, ForeignKey("matches.id", ondelete="SET NULL"))
    error_log = Column(Text)
    created_at = Column(DateTime(timezone=True), server_default=func.now())
    finished_at = Column(DateTime(timezone=True))


class CompetitionSlot(Base):
    __tablename__ = "competition_slots"
    __table_args__ = (
        UniqueConstraint("competition_id", "team_id", name="uq_competition_slots_competition_team"),
        Index("ix_competition_slots_competition", "competition_id"),
        Index("ix_competition_slots_team", "team_id"),
    )

    id = Column(Integer, primary_key=True)
    competition_id = Column(Integer, ForeignKey("competitions.id", ondelete="CASCADE"), nullable=False)
    team_id = Column(Integer, ForeignKey("teams.id", ondelete="CASCADE"), nullable=False)
    selected_submission_id = Column(Integer, ForeignKey("submissions.id", ondelete="SET NULL"))
    updated_at = Column(
        DateTime(timezone=True),
        server_default=func.now(),
        default=lambda: datetime.now(timezone.utc),
        onupdate=func.now(),
    )


os.makedirs(settings.artifact_dir, exist_ok=True)
os.makedirs(settings.upload_dir, exist_ok=True)
os.makedirs(settings.match_data_dir, exist_ok=True)


async def backfill_submission_selection(db: AsyncSession) -> None:
    await db.execute(
        text("UPDATE submissions SET name = '代码 #' || id WHERE name IS NULL OR TRIM(name) = ''")
    )
    await db.execute(
        text("UPDATE submissions SET is_dispatched = FALSE WHERE is_dispatched IS NULL")
    )
    await db.execute(
        text("UPDATE submissions SET is_dispatched = FALSE WHERE status IS NULL OR status != 'ready'")
    )

    ready_rows = await db.execute(
        select(Submission.id, Submission.team_id)
        .where(Submission.status == "ready", Submission.is_dispatched.is_(True))
        .order_by(
            Submission.team_id,
            Submission.compiled_at.desc().nullslast(),
            Submission.uploaded_at.desc(),
            Submission.id.desc(),
        )
    )
    duplicate_ids: list[int] = []
    seen_teams: set[int] = set()
    for submission_id, team_id in ready_rows.all():
        if team_id in seen_teams:
            duplicate_ids.append(submission_id)
            continue
        seen_teams.add(team_id)

    if duplicate_ids:
        await db.execute(
            update(Submission)
            .where(Submission.id.in_(duplicate_ids))
            .values(is_dispatched=False)
        )
    await db.commit()


async def recover_inflight_matches(db: AsyncSession) -> None:
    running_rows = await db.execute(
        select(Match.id).where(Match.status == "running")
    )
    running_ids = [match_id for match_id, in running_rows.all()]
    if not running_ids:
        return

    logger.warning("Recovering stale running matches on startup: %s", running_ids)
    await db.execute(
        delete(SubmissionMatchLog).where(SubmissionMatchLog.match_id.in_(running_ids))
    )
    await db.execute(
        update(MatchParticipant)
        .where(MatchParticipant.match_id.in_(running_ids))
        .values(score=None)
    )
    await db.execute(
        update(Match)
        .where(Match.id.in_(running_ids))
        .values(
            status="pending",
            score_a=None,
            score_b=None,
            error_log=None,
            finished_at=None,
        )
    )
    await db.commit()


def _effective_deadline(competition: Competition) -> datetime:
    return competition.submission_deadline or competition.scheduled_at


async def _next_competition_guard_window(db: AsyncSession) -> datetime | None:
    result = await db.execute(
        select(Competition.scheduled_at)
        .where(Competition.status == "scheduled")
        .order_by(Competition.scheduled_at.asc(), Competition.id.asc())
        .limit(1)
    )
    return result.scalar_one_or_none()


async def _schedule_due_competition(db: AsyncSession) -> bool:
    now = datetime.now(timezone.utc)
    result = await db.execute(
        select(Competition)
        .where(
            Competition.status == "scheduled",
            Competition.scheduled_at <= now,
        )
        .order_by(Competition.scheduled_at.asc(), Competition.id.asc())
        .limit(1)
    )
    competition = result.scalar_one_or_none()
    if competition is None:
        return False

    slot_rows = await db.execute(
        select(CompetitionSlot, Submission)
        .join(Submission, Submission.id == CompetitionSlot.selected_submission_id, isouter=True)
        .where(CompetitionSlot.competition_id == competition.id)
        .order_by(CompetitionSlot.id.asc())
    )

    ready_entries: list[tuple[CompetitionSlot, Submission]] = []
    skipped: list[str] = []
    for slot, submission in slot_rows.all():
        if slot.selected_submission_id is None:
            continue
        if submission is None:
            skipped.append(f"team {slot.team_id}: 选中的提交不存在")
            continue
        if submission.team_id != slot.team_id:
            skipped.append(f"team {slot.team_id}: 选中的提交不属于该队伍")
            continue
        if submission.status != "ready" or not submission.artifact_path:
            skipped.append(f"team {slot.team_id}: 提交 #{submission.id} 当前不是可运行的 ready 状态")
            continue
        ready_entries.append((slot, submission))

    if not ready_entries:
        competition.status = "error"
        competition.error_log = "\n".join(
            ["赛事到时后没有可运行的 ready 提交。", *skipped]
        ).strip()
        competition.finished_at = now
        await db.commit()
        logger.warning("Competition %d could not start: no ready participants", competition.id)
        return True

    first_submission = ready_entries[0][1]
    second_submission = ready_entries[1][1] if len(ready_entries) >= 2 else first_submission
    match = Match(
        mode="event",
        submission_a_id=first_submission.id,
        submission_b_id=second_submission.id,
        status="pending",
        scheduled_at=competition.scheduled_at,
    )
    db.add(match)
    await db.flush()

    db.add_all([
        MatchParticipant(
            match_id=match.id,
            submission_id=submission.id,
            team_id=slot.team_id,
            player_token=secrets.token_urlsafe(24),
        )
        for slot, submission in ready_entries
    ])

    competition.match_id = match.id
    competition.status = "running"
    competition.error_log = "\n".join(skipped).strip() or None
    await db.commit()
    logger.info(
        "Scheduled competition %d into match %d with submissions=%s",
        competition.id,
        match.id,
        [submission.id for _, submission in ready_entries],
    )
    return True


async def competition_scheduler_loop():
    while True:
        try:
            async with AsyncSessionLocal() as db:
                while await _schedule_due_competition(db):
                    pass
        except Exception:
            logger.exception("competition_scheduler_loop error")

        await asyncio.sleep(10)


async def compile_loop():
    """Pick up pending submissions and build Docker images."""
    while True:
        try:
            async with AsyncSessionLocal() as db:
                result = await db.execute(
                    select(Submission).where(Submission.status == "pending").limit(1)
                )
                sub = result.scalar_one_or_none()

                if sub is not None:
                    logger.info("Building submission image %d", sub.id)
                    await db.execute(
                        update(Submission)
                        .where(Submission.id == sub.id)
                        .values(
                            status="compiling",
                            error_log="编译中...",
                        )
                    )
                    await db.commit()

                    loop = asyncio.get_running_loop()

                    async def persist_compile_log(submission_id: int, compile_log: str) -> None:
                        async with AsyncSessionLocal() as progress_db:
                            await progress_db.execute(
                                update(Submission)
                                .where(Submission.id == submission_id)
                                .where(Submission.status == "compiling")
                                .values(error_log=compile_log)
                            )
                            await progress_db.commit()

                    def on_progress(compile_log: str) -> None:
                        future = asyncio.run_coroutine_threadsafe(
                            persist_compile_log(sub.id, compile_log),
                            loop,
                        )
                        def _swallow_future_exception(done_future) -> None:
                            try:
                                exc = done_future.exception()
                            except Exception:
                                logger.exception("persist_compile_log callback failed for submission %d", sub.id)
                                return
                            if exc is not None:
                                logger.exception(
                                    "persist_compile_log callback failed for submission %d",
                                    sub.id,
                                    exc_info=exc,
                                )
                        future.add_done_callback(_swallow_future_exception)

                    success, compile_log, image_ref = await loop.run_in_executor(
                        None, compile_submission, sub.id, on_progress
                    )

                    await db.execute(
                        update(Submission)
                        .where(Submission.id == sub.id)
                        .values(
                            status="ready" if success else "failed",
                            error_log=compile_log or None,
                            artifact_path=image_ref if success else None,
                            language="docker",
                            compiled_at=datetime.now(timezone.utc),
                        )
                    )
                    await db.commit()
                    logger.info("Submission %d: %s", sub.id, "ready" if success else "failed")
        except Exception:
            logger.exception("compile_loop error")

        await asyncio.sleep(5)


async def arena_loop():
    """Schedule one live arena match containing every team with a ready submission."""
    while True:
        try:
            async with AsyncSessionLocal() as db:
                active_result = await db.execute(
                    select(Match.id)
                    .where(Match.status.in_(["pending", "running"]))
                    .limit(1)
                )
                if active_result.scalar_one_or_none() is not None:
                    await asyncio.sleep(30)
                    continue

                next_competition_time = await _next_competition_guard_window(db)
                if next_competition_time is not None:
                    now = datetime.now(timezone.utc)
                    guard_until = now + timedelta(seconds=GAME_TIMEOUT_SECONDS)
                    if next_competition_time <= guard_until:
                        await asyncio.sleep(30)
                        continue

                # Use the submission explicitly dispatched by each team.
                result = await db.execute(
                    select(Submission)
                    .where(
                        Submission.status == "ready",
                        Submission.is_dispatched.is_(True),
                    )
                    .order_by(
                        Submission.team_id,
                        Submission.compiled_at.desc().nullslast(),
                        Submission.uploaded_at.desc(),
                        Submission.id.desc(),
                    )
                )
                all_ready = result.scalars().all()

                # Defensive dedupe in case legacy data or a race leaves multiple
                # dispatched submissions for the same team.
                seen_teams: set[int] = set()
                ready: list[Submission] = []
                for sub in all_ready:
                    if sub.team_id not in seen_teams:
                        seen_teams.add(sub.team_id)
                        ready.append(sub)

                if len(ready) >= 1:
                    random.shuffle(ready)
                    a = ready[0]
                    b = ready[1] if len(ready) >= 2 else ready[0]
                    match = Match(
                        mode="arena",
                        submission_a_id=a.id,
                        submission_b_id=b.id,
                        status="pending",
                    )
                    db.add(match)
                    await db.flush()
                    # The player_token doubles as the live-server auth credential
                    # (it gets loaded into the game server's TOKENS allowlist), so
                    # it must be an unguessable secret — never derivable from public
                    # ids. Anyone who learns it could bind to the live WebSocket as
                    # that team's agent.
                    participants = [
                        MatchParticipant(
                            match_id=match.id,
                            submission_id=sub.id,
                            team_id=sub.team_id,
                            player_token=secrets.token_urlsafe(24),
                        )
                        for sub in ready
                    ]
                    db.add_all(participants)
                    await db.commit()
                    logger.info(
                        "Scheduled live arena match %d: submissions=%s",
                        match.id,
                        [sub.id for sub in ready],
                    )
        except Exception:
            logger.exception("arena_loop error")

        await asyncio.sleep(30)


async def _prune_submission_logs(db, submission_ids):
    """Keep only the most recent LOG_RETENTION_PER_SUBMISSION rows per submission."""
    for sub_id in submission_ids:
        keep_result = await db.execute(
            select(SubmissionMatchLog.id)
            .where(SubmissionMatchLog.submission_id == sub_id)
            .order_by(SubmissionMatchLog.id.desc())
            .limit(LOG_RETENTION_PER_SUBMISSION)
        )
        keep_ids = [row[0] for row in keep_result.all()]
        if len(keep_ids) >= LOG_RETENTION_PER_SUBMISSION:
            await db.execute(
                delete(SubmissionMatchLog)
                .where(SubmissionMatchLog.submission_id == sub_id)
                .where(SubmissionMatchLog.id.notin_(keep_ids))
            )
    await db.commit()


async def _persist_match_logs(match_id, agents, agent_logs):
    """Best-effort persistence of per-agent container output.

    Runs in its own transaction, decoupled from the match-completion commit, so
    a log-write failure can never roll the match back into a stuck "running"
    state (the runner loop only re-picks "pending" rows)."""
    try:
        async with AsyncSessionLocal() as db:
            for agent in agents:
                db.add(SubmissionMatchLog(
                    match_id=match_id,
                    submission_id=agent.submission_id,
                    team_id=agent.team_id,
                    log=agent_logs.get(agent.submission_id, ""),
                ))
            await db.commit()
            await _prune_submission_logs(db, {agent.submission_id for agent in agents})
    except Exception:
        logger.exception("failed to persist match logs for match %d", match_id)


async def match_runner_loop():
    """Pick up pending matches and run them."""
    while True:
        try:
            async with AsyncSessionLocal() as db:
                result = await db.execute(
                    select(Match)
                    .where(Match.status == "pending")
                    .order_by(Match.scheduled_at.asc(), Match.id.asc())
                    .limit(1)
                )
                match = result.scalar_one_or_none()

                if match is not None:
                    logger.info("Running match %d", match.id)
                    await db.execute(
                        update(Match).where(Match.id == match.id).values(status="running")
                    )
                    await db.commit()

                    participant_result = await db.execute(
                        select(MatchParticipant)
                        .where(MatchParticipant.match_id == match.id)
                        .order_by(MatchParticipant.id)
                    )
                    participant_rows = list(participant_result.scalars().all())

                    if participant_rows:
                        submission_ids = [row.submission_id for row in participant_rows]
                        sub_result = await db.execute(
                            select(Submission).where(Submission.id.in_(submission_ids))
                        )
                        submissions = {sub.id: sub for sub in sub_result.scalars().all()}
                        agents = [
                            MatchAgent(
                                submission_id=row.submission_id,
                                team_id=row.team_id,
                                image_ref=submissions[row.submission_id].artifact_path or "",
                                token=row.player_token,
                                player_name=submissions[row.submission_id].name or "",
                            )
                            for row in participant_rows
                            if row.submission_id in submissions
                        ]
                    else:
                        # Legacy pending rows created before match_participants existed.
                        sub_result = await db.execute(
                            select(Submission).where(
                                Submission.id.in_([match.submission_a_id, match.submission_b_id])
                            )
                        )
                        submissions = {sub.id: sub for sub in sub_result.scalars().all()}
                        agents = [
                            MatchAgent(
                                submission_id=submissions[match.submission_a_id].id,
                                team_id=submissions[match.submission_a_id].team_id,
                                image_ref=submissions[match.submission_a_id].artifact_path or "",
                                token=secrets.token_urlsafe(24),
                                player_name=submissions[match.submission_a_id].name or "",
                            ),
                            MatchAgent(
                                submission_id=submissions[match.submission_b_id].id,
                                team_id=submissions[match.submission_b_id].team_id,
                                image_ref=submissions[match.submission_b_id].artifact_path or "",
                                token=secrets.token_urlsafe(24),
                                player_name=submissions[match.submission_b_id].name or "",
                            ),
                        ]

                    competition_result = await db.execute(
                        select(Competition).where(Competition.match_id == match.id).limit(1)
                    )
                    competition = competition_result.scalar_one_or_none()

                    scores, error_log, agent_logs = await asyncio.get_running_loop().run_in_executor(
                        None,
                        run_match,
                        match.id,
                        agents,
                        competition.live_server_image if competition is not None else None,
                    )

                    score_a = scores.get(match.submission_a_id) if scores is not None else None
                    score_b = scores.get(match.submission_b_id) if scores is not None else None
                    if scores is not None:
                        for row in participant_rows:
                            await db.execute(
                                update(MatchParticipant)
                                .where(MatchParticipant.id == row.id)
                                .values(score=scores.get(row.submission_id))
                            )

                    await db.execute(
                        update(Match)
                        .where(Match.id == match.id)
                        .values(
                            status="finished" if scores is not None else "error",
                            score_a=score_a,
                            score_b=score_b,
                            error_log=error_log or None,
                            finished_at=datetime.now(timezone.utc),
                        )
                    )

                    if competition is not None:
                        combined_error_log = "\n".join(
                            part for part in [competition.error_log, error_log] if part
                        ).strip() or None
                        await db.execute(
                            update(Competition)
                            .where(Competition.id == competition.id)
                            .values(
                                status="finished" if scores is not None else "error",
                                error_log=combined_error_log,
                                finished_at=datetime.now(timezone.utc),
                            )
                        )
                    # Match completion is the source of truth for the leaderboard
                    # and the runner loop, so commit it before touching logs.
                    await db.commit()
                    logger.info("Match %d finished: %s", match.id, scores)

                    # Per-agent container output, persisted out-of-band so a log
                    # failure cannot strand the (already finished) match.
                    await _persist_match_logs(match.id, agents, agent_logs)
        except Exception:
            logger.exception("match_runner_loop error")

        await asyncio.sleep(10)


async def ensure_schema():
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

        def add_submission_columns(sync_conn) -> None:
            def _refresh_columns(table_name: str):
                inspector = inspect(sync_conn)
                return {column["name"]: column for column in inspector.get_columns(table_name)}

            def ensure_column(column_name: str, ddl: str) -> None:
                columns = set(_refresh_columns("submissions"))
                if column_name in columns:
                    return
                try:
                    sync_conn.execute(text(ddl))
                except Exception:
                    columns = set(_refresh_columns("submissions"))
                    if column_name not in columns:
                        raise

            def ensure_bigint(table_name: str, column_name: str) -> None:
                columns = _refresh_columns(table_name)
                if column_name not in columns:
                    return
                if "BIGINT" in str(columns[column_name]["type"]).upper():
                    return
                try:
                    sync_conn.execute(
                        text(f"ALTER TABLE {table_name} ALTER COLUMN {column_name} TYPE BIGINT")
                    )
                except Exception:
                    columns = _refresh_columns(table_name)
                    if column_name not in columns or "BIGINT" not in str(columns[column_name]["type"]).upper():
                        raise

            ensure_column("name", "ALTER TABLE submissions ADD COLUMN name VARCHAR(64)")
            ensure_column(
                "is_dispatched",
                "ALTER TABLE submissions ADD COLUMN is_dispatched BOOLEAN DEFAULT FALSE",
            )

            if sync_conn.dialect.name == "postgresql":
                ensure_bigint("matches", "score_a")
                ensure_bigint("matches", "score_b")
                ensure_bigint("match_participants", "score")

        await conn.run_sync(add_submission_columns)

    async with AsyncSessionLocal() as db:
        await backfill_submission_selection(db)


async def main():
    logger.info("Evaluator starting")
    await ensure_schema()
    cleanup_runtime_containers()
    async with AsyncSessionLocal() as db:
        await recover_inflight_matches(db)
    await asyncio.gather(
        compile_loop(),
        competition_scheduler_loop(),
        arena_loop(),
        match_runner_loop(),
    )


if __name__ == "__main__":
    asyncio.run(main())
