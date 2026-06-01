import uuid

from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.auth import create_token, hash_password, verify_password
from app.config import settings
from app.database import get_db
from app.dependencies import AuthActor, get_current_actor, require_admin
from app.models import Team
from app.schemas import CurrentUserOut, TeamAccountOut, TeamLogin, TeamRegister, TokenResponse

router = APIRouter()


@router.post("/register", response_model=TokenResponse, status_code=status.HTTP_201_CREATED)
async def register(body: TeamRegister, db: AsyncSession = Depends(get_db)):
    if body.email == settings.admin_email or body.name == settings.admin_name:
        raise HTTPException(status_code=409, detail="该邮箱或名称保留给管理员账号")

    existing = await db.execute(
        select(Team).where((Team.email == body.email) | (Team.name == body.name))
    )
    if existing.scalars().first() is not None:
        raise HTTPException(status_code=409, detail="队伍名称或邮箱已被注册")

    team = Team(
        name=body.name,
        email=body.email,
        password_hash=hash_password(body.password),
        game_token=str(uuid.uuid4()),
    )
    db.add(team)
    await db.commit()
    await db.refresh(team)

    token = create_token(str(team.id), "team")
    return TokenResponse(
        access_token=token,
        game_token=team.game_token,
        id=team.id,
        role="team",
        display_name=team.name,
        email=team.email,
    )


@router.post("/login", response_model=TokenResponse)
async def login(body: TeamLogin, db: AsyncSession = Depends(get_db)):
    # Admin login
    if body.email == settings.admin_email:
        if body.password != settings.admin_password:
            raise HTTPException(status_code=401, detail="邮箱或密码错误")

        admin_result = await db.execute(select(Team).where(Team.email == settings.admin_email))
        admin_team = admin_result.scalar_one_or_none()
        if admin_team is None:
            raise HTTPException(status_code=503, detail="管理员账号尚未初始化完成")

        token = create_token(settings.admin_email, "admin", expires_hours=8)
        return TokenResponse(
            access_token=token,
            game_token=admin_team.game_token,
            id=admin_team.id,
            role="admin",
            display_name=admin_team.name,
            email=admin_team.email,
        )

    result = await db.execute(select(Team).where(Team.email == body.email))
    team = result.scalar_one_or_none()
    if not team or not verify_password(body.password, team.password_hash):
        raise HTTPException(status_code=401, detail="邮箱或密码错误")

    token = create_token(str(team.id), "team")
    return TokenResponse(
        access_token=token,
        game_token=team.game_token,
        id=team.id,
        role="team",
        display_name=team.name,
        email=team.email,
    )


@router.get("/me", response_model=CurrentUserOut)
async def me(actor: AuthActor = Depends(get_current_actor)):
    return CurrentUserOut(
        role=actor.role,
        id=actor.id,
        display_name=actor.display_name,
        email=actor.email,
        game_token=actor.game_token,
    )


@router.get("/admin/accounts", response_model=list[TeamAccountOut], dependencies=[Depends(require_admin)])
async def list_team_accounts(db: AsyncSession = Depends(get_db)):
    result = await db.execute(
        select(Team).order_by(Team.name.asc(), Team.id.asc())
    )
    return [
        TeamAccountOut(id=team.id, name=team.name, email=team.email)
        for team in result.scalars().all()
    ]
