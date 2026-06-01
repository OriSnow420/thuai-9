import uuid
from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from sqlalchemy import inspect, select, text, update

from app.auth import hash_password
from app.config import settings
from app.database import AsyncSessionLocal, Base, engine
from app.models import Submission, Team
from app.routers import competitions, leaderboard, matches, submissions, teams


async def ensure_schema() -> None:
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

    async with AsyncSessionLocal() as session:
        await session.execute(
            text("UPDATE submissions SET name = '代码 #' || id WHERE name IS NULL OR TRIM(name) = ''")
        )
        await session.execute(
            text("UPDATE submissions SET is_dispatched = FALSE WHERE is_dispatched IS NULL")
        )
        await session.execute(
            text("UPDATE submissions SET is_dispatched = FALSE WHERE status IS NULL OR status != 'ready'")
        )

        ready_rows = await session.execute(
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
            await session.execute(
                update(Submission)
                .where(Submission.id.in_(duplicate_ids))
                .values(is_dispatched=False)
            )
        await session.commit()


async def ensure_admin_team() -> None:
    """Bind the env-configured admin identity to a real Team row so it can use
    the same submission pipeline as normal accounts without losing admin-only
    privileges."""
    async with AsyncSessionLocal() as session:
        result = await session.execute(
            select(Team)
            .where((Team.email == settings.admin_email) | (Team.name == settings.admin_name))
            .order_by(Team.id.asc())
        )
        matches = result.scalars().all()

        if len(matches) > 1:
            raise RuntimeError(
                "管理员邮箱/名称与多个现有账号冲突，请先清理 teams 表中的冲突记录后再启动"
            )

        if not matches:
            session.add(
                Team(
                    name=settings.admin_name,
                    email=settings.admin_email,
                    password_hash=hash_password(settings.admin_password),
                    game_token=str(uuid.uuid4()),
                )
            )
            await session.commit()
            return

        admin_team = matches[0]
        changed = False
        if admin_team.name != settings.admin_name:
            admin_team.name = settings.admin_name
            changed = True
        if admin_team.email != settings.admin_email:
            admin_team.email = settings.admin_email
            changed = True
        if not admin_team.game_token:
            admin_team.game_token = str(uuid.uuid4())
            changed = True

        if changed:
            await session.commit()


@asynccontextmanager
async def lifespan(app: FastAPI):
    await ensure_schema()
    await ensure_admin_team()
    yield


app = FastAPI(title="THUAI-9 Submission API", lifespan=lifespan, docs_url=None, redoc_url=None)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["https://thuasta.org", "http://localhost", "http://localhost:3000"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(teams.router, tags=["teams"])
app.include_router(submissions.router, prefix="/submissions", tags=["submissions"])
app.include_router(matches.router, prefix="/matches", tags=["matches"])
app.include_router(competitions.router, prefix="/competitions", tags=["competitions"])
app.include_router(leaderboard.router, prefix="/leaderboard", tags=["leaderboard"])


@app.get("/health")
async def health():
    return {"status": "ok"}
