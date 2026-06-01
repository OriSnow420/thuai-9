from __future__ import annotations

from datetime import datetime, timezone

from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy import case, func, select
from sqlalchemy.ext.asyncio import AsyncSession

from app.database import get_db
from app.dependencies import AuthActor, get_current_actor, require_admin
from app.models import Competition, CompetitionSlot, MatchParticipant, Submission, Team
from app.schemas import (
    CompetitionCreateRequest,
    CompetitionDetailOut,
    CompetitionEnrollRequest,
    CompetitionSlotOut,
    CompetitionSummaryOut,
)
from app.score_utils import serialize_score

router = APIRouter()


def _effective_deadline(competition: Competition) -> datetime:
    return competition.submission_deadline or competition.scheduled_at


async def _load_slots(db: AsyncSession, competition: Competition) -> list[CompetitionSlotOut]:
    rows = await db.execute(
        select(CompetitionSlot, Team, Submission)
        .join(Team, Team.id == CompetitionSlot.team_id)
        .join(Submission, Submission.id == CompetitionSlot.selected_submission_id, isouter=True)
        .where(CompetitionSlot.competition_id == competition.id)
        .order_by(Team.name.asc(), Team.id.asc())
    )

    score_by_submission: dict[int, int] = {}
    if competition.match_id is not None:
        score_rows = await db.execute(
            select(MatchParticipant.submission_id, MatchParticipant.score)
            .where(MatchParticipant.match_id == competition.match_id)
        )
        score_by_submission = {
            submission_id: score
            for submission_id, score in score_rows.all()
            if submission_id is not None and score is not None
        }

    slots: list[CompetitionSlotOut] = []
    for slot, team, submission in rows.all():
        slots.append(
            CompetitionSlotOut(
                team_id=team.id,
                team_name=team.name,
                selected_submission_id=submission.id if submission is not None else None,
                selected_submission_name=submission.name if submission is not None else None,
                selected_submission_status=submission.status if submission is not None else None,
                updated_at=slot.updated_at,
                score=serialize_score(score_by_submission.get(submission.id) if submission is not None else None),
            )
        )
    return slots


async def _serialize_competition(
    db: AsyncSession,
    competition: Competition,
    actor: AuthActor,
    include_slots: bool,
) -> CompetitionSummaryOut | CompetitionDetailOut:
    slots = await _load_slots(db, competition)
    current_slot = None
    if actor.team is not None:
        current_slot = next((slot for slot in slots if slot.team_id == actor.team.id), None)

    payload = dict(
        id=competition.id,
        name=competition.name,
        description=competition.description,
        status=competition.status,
        scheduled_at=competition.scheduled_at,
        submission_deadline=competition.submission_deadline,
        effective_deadline=_effective_deadline(competition),
        live_server_image=competition.live_server_image,
        created_by_name=competition.created_by_name,
        created_by_email=competition.created_by_email,
        match_id=competition.match_id,
        eligible_team_count=len(slots),
        participant_count=sum(1 for slot in slots if slot.selected_submission_id is not None),
        is_eligible=current_slot is not None,
        current_submission_id=current_slot.selected_submission_id if current_slot is not None else None,
    )
    if include_slots:
        return CompetitionDetailOut(
            **payload,
            error_log=competition.error_log,
            finished_at=competition.finished_at,
            slots=slots,
        )
    return CompetitionSummaryOut(**payload)


async def _get_competition_or_404(db: AsyncSession, competition_id: int) -> Competition:
    result = await db.execute(
        select(Competition).where(Competition.id == competition_id)
    )
    competition = result.scalar_one_or_none()
    if competition is None:
        raise HTTPException(status_code=404, detail="赛事不存在")
    return competition


@router.get("/", response_model=list[CompetitionSummaryOut])
async def list_competitions(
    actor: AuthActor = Depends(get_current_actor),
    db: AsyncSession = Depends(get_db),
):
    result = await db.execute(
        select(Competition)
        .order_by(
            case(
                (Competition.status == "scheduled", 0),
                (Competition.status == "running", 1),
                (Competition.status == "finished", 2),
                else_=3,
            ),
            Competition.scheduled_at.asc(),
            Competition.id.asc(),
        )
        .limit(200)
    )
    competitions = result.scalars().all()
    return [
        await _serialize_competition(db, competition, actor, include_slots=False)
        for competition in competitions
    ]


@router.get("/{competition_id}", response_model=CompetitionDetailOut)
async def get_competition_detail(
    competition_id: int,
    actor: AuthActor = Depends(get_current_actor),
    db: AsyncSession = Depends(get_db),
):
    competition = await _get_competition_or_404(db, competition_id)
    detail = await _serialize_competition(db, competition, actor, include_slots=True)
    return detail


@router.post("/admin", response_model=CompetitionDetailOut, status_code=status.HTTP_201_CREATED, dependencies=[Depends(require_admin)])
async def create_competition(
    body: CompetitionCreateRequest,
    actor: AuthActor = Depends(get_current_actor),
    db: AsyncSession = Depends(get_db),
):
    now = datetime.now(timezone.utc)
    if body.scheduled_at <= now:
        raise HTTPException(status_code=400, detail="赛事开始时间必须晚于当前时间")
    if body.submission_deadline is not None and body.submission_deadline > body.scheduled_at:
        raise HTTPException(status_code=400, detail="提交截止时间不能晚于赛事开始时间")

    team_rows = await db.execute(
        select(Team).where(Team.id.in_(body.eligible_team_ids)).order_by(Team.id.asc())
    )
    teams = team_rows.scalars().all()
    found_ids = {team.id for team in teams}
    missing_ids = [team_id for team_id in body.eligible_team_ids if team_id not in found_ids]
    if missing_ids:
        raise HTTPException(status_code=400, detail=f"以下账号不存在: {', '.join(map(str, missing_ids))}")

    competition = Competition(
        name=body.name.strip(),
        description=(body.description or "").strip() or None,
        status="scheduled",
        scheduled_at=body.scheduled_at,
        submission_deadline=body.submission_deadline,
        live_server_image=body.live_server_image.strip(),
        created_by_email=actor.email,
        created_by_name=actor.display_name,
    )
    db.add(competition)
    await db.flush()

    db.add_all([
        CompetitionSlot(
            competition_id=competition.id,
            team_id=team.id,
            selected_submission_id=None,
        )
        for team in teams
    ])
    await db.commit()

    competition = await _get_competition_or_404(db, competition.id)
    return await _serialize_competition(db, competition, actor, include_slots=True)


@router.post("/{competition_id}/enroll", response_model=CompetitionDetailOut)
async def enroll_competition(
    competition_id: int,
    body: CompetitionEnrollRequest,
    actor: AuthActor = Depends(get_current_actor),
    db: AsyncSession = Depends(get_db),
):
    if actor.team is None:
        raise HTTPException(status_code=403, detail="仅队伍账号可报名赛事")

    competition = await _get_competition_or_404(db, competition_id)
    now = datetime.now(timezone.utc)
    if competition.status != "scheduled":
        raise HTTPException(status_code=400, detail="赛事当前不接受报名")
    if now >= _effective_deadline(competition):
        raise HTTPException(status_code=400, detail="赛事报名已截止")

    slot_result = await db.execute(
        select(CompetitionSlot)
        .where(
            CompetitionSlot.competition_id == competition.id,
            CompetitionSlot.team_id == actor.team.id,
        )
    )
    slot = slot_result.scalar_one_or_none()
    if slot is None:
        raise HTTPException(status_code=403, detail="你没有该赛事的参赛资格")

    submission_result = await db.execute(
        select(Submission)
        .where(
            Submission.id == body.submission_id,
            Submission.team_id == actor.team.id,
        )
    )
    submission = submission_result.scalar_one_or_none()
    if submission is None:
        raise HTTPException(status_code=404, detail="提交不存在")
    if submission.status != "ready":
        raise HTTPException(status_code=400, detail="只有 ready 状态的代码可以参赛")

    slot.selected_submission_id = submission.id
    await db.commit()

    competition = await _get_competition_or_404(db, competition.id)
    return await _serialize_competition(db, competition, actor, include_slots=True)


@router.delete("/{competition_id}/enroll", response_model=CompetitionDetailOut)
async def withdraw_competition(
    competition_id: int,
    actor: AuthActor = Depends(get_current_actor),
    db: AsyncSession = Depends(get_db),
):
    if actor.team is None:
        raise HTTPException(status_code=403, detail="仅队伍账号可退出赛事")

    competition = await _get_competition_or_404(db, competition_id)
    now = datetime.now(timezone.utc)
    if competition.status != "scheduled":
        raise HTTPException(status_code=400, detail="赛事当前不允许修改报名")
    if now >= _effective_deadline(competition):
        raise HTTPException(status_code=400, detail="赛事报名已截止")

    slot_result = await db.execute(
        select(CompetitionSlot)
        .where(
            CompetitionSlot.competition_id == competition.id,
            CompetitionSlot.team_id == actor.team.id,
        )
    )
    slot = slot_result.scalar_one_or_none()
    if slot is None:
        raise HTTPException(status_code=403, detail="你没有该赛事的参赛资格")

    slot.selected_submission_id = None
    await db.commit()

    competition = await _get_competition_or_404(db, competition.id)
    return await _serialize_competition(db, competition, actor, include_slots=True)
