from dataclasses import dataclass

from fastapi import Depends, HTTPException, status
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer

from app.auth import decode_token
from app.config import settings
from app.database import AsyncSession, get_db
from app.models import Team

bearer = HTTPBearer()
optional_bearer = HTTPBearer(auto_error=False)


@dataclass(frozen=True)
class AuthActor:
    role: str
    team: Team | None = None

    @property
    def id(self) -> int | None:
        return self.team.id if self.team is not None else None

    @property
    def display_name(self) -> str:
        return self.team.name if self.team is not None else settings.admin_name

    @property
    def email(self) -> str:
        return self.team.email if self.team is not None else settings.admin_email

    @property
    def game_token(self) -> str:
        return self.team.game_token if self.team is not None else ""


async def _resolve_actor(token: str, db: AsyncSession) -> AuthActor:
    try:
        payload = decode_token(token)
    except ValueError:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Invalid token")

    role = payload.get("role")
    subject = payload.get("sub")
    if role == "admin":
        if subject != settings.admin_email:
            raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Invalid token")
        from sqlalchemy import select

        result = await db.execute(select(Team).where(Team.email == settings.admin_email))
        admin_team = result.scalar_one_or_none()
        if admin_team is None:
            raise HTTPException(
                status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                detail="Admin team not initialized",
            )
        return AuthActor(role="admin", team=admin_team)

    if role != "team" or not subject:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Invalid token")

    from sqlalchemy import select

    try:
        team_id = int(subject)
    except (TypeError, ValueError):
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Invalid token")

    result = await db.execute(select(Team).where(Team.id == team_id))
    team = result.scalar_one_or_none()
    if team is None:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Team not found")
    return AuthActor(role="team", team=team)


async def get_current_actor(
    credentials: HTTPAuthorizationCredentials = Depends(bearer),
    db: AsyncSession = Depends(get_db),
) -> AuthActor:
    return await _resolve_actor(credentials.credentials, db)


async def get_optional_actor(
    credentials: HTTPAuthorizationCredentials | None = Depends(optional_bearer),
    db: AsyncSession = Depends(get_db),
) -> AuthActor | None:
    if credentials is None:
        return None
    return await _resolve_actor(credentials.credentials, db)


async def get_current_team(
    actor: AuthActor = Depends(get_current_actor),
) -> Team:
    if actor.team is None:
        raise HTTPException(status_code=status.HTTP_403_FORBIDDEN, detail="Team only")
    return actor.team


async def require_admin(
    actor: AuthActor = Depends(get_current_actor),
) -> None:
    if actor.role != "admin":
        raise HTTPException(status_code=status.HTTP_403_FORBIDDEN, detail="Admin only")
