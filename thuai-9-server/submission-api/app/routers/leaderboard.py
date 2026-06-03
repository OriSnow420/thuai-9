from decimal import Decimal
from enum import Enum

from fastapi import APIRouter, Depends, Query
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.database import get_db
from app.models import Match, MatchParticipant, Submission, Team
from app.schemas import LeaderboardEntry
from app.score_utils import average_score_value, serialize_average_score, serialize_score

router = APIRouter()


class LeaderboardScope(str, Enum):
    # Every submission that has ever played a finished match.
    all = "all"
    # Only the submission each team currently has fielded (is_dispatched). This
    # is exactly the set the evaluator auto-matches, so superseded iterations
    # drop off the board and the current code can be judged on its own.
    active = "active"


@router.get("/", response_model=list[LeaderboardEntry])
async def leaderboard(
    scope: LeaderboardScope = Query(LeaderboardScope.all),
    db: AsyncSession = Depends(get_db),
):
    submission_query = (
        select(Submission.id, Submission.name, Team.name)
        .join(Team, Team.id == Submission.team_id)
    )
    if scope is LeaderboardScope.active:
        submission_query = submission_query.where(Submission.is_dispatched.is_(True))

    submissions_result = await db.execute(submission_query)
    submissions = {
        row[0]: {
            "submission_name": row[1],
            "team_name": row[2],
        }
        for row in submissions_result.all()
    }

    stats: dict[int, list[int]] = {}
    participant_rows = await db.execute(
        select(
            MatchParticipant.match_id,
            MatchParticipant.submission_id,
            MatchParticipant.score,
        )
        .join(Match, Match.id == MatchParticipant.match_id)
        .where(
            Match.status == "finished",
            MatchParticipant.score.is_not(None),
        )
    )
    matches_with_participants: set[int] = set()
    for match_id, submission_id, score in participant_rows.all():
        if submission_id not in submissions or score is None:
            continue
        matches_with_participants.add(match_id)
        stats.setdefault(submission_id, []).append(score)

    legacy_matches_result = await db.execute(
        select(
            Match.id,
            Match.submission_a_id,
            Match.submission_b_id,
            Match.score_a,
            Match.score_b,
        )
        .where(Match.status == "finished")
    )
    for match_id, submission_a_id, submission_b_id, score_a, score_b in legacy_matches_result.all():
        if match_id in matches_with_participants:
            continue

        # Legacy two-player rows from before match_participants existed.
        if submission_a_id in submissions and score_a is not None:
            stats.setdefault(submission_a_id, []).append(score_a)
        if submission_b_id in submissions and score_b is not None:
            stats.setdefault(submission_b_id, []).append(score_b)

    entries: list[tuple[Decimal, int, LeaderboardEntry]] = []
    for submission_id, scores in stats.items():
        if not scores:
            continue
        total_score = sum(scores)
        total_matches = len(scores)
        submission = submissions[submission_id]
        average_value = average_score_value(total_score, total_matches)
        entry = LeaderboardEntry(
                submission_id=submission_id,
                submission_name=submission["submission_name"],
                team_name=submission["team_name"],
                total_score=serialize_score(total_score) or "0",
                average_score=serialize_average_score(total_score, total_matches),
                best_score=serialize_score(max(scores) if scores else None),
                total_matches=total_matches,
        )
        entries.append((average_value, total_score, entry))
    entries.sort(
        key=lambda item: (
            -item[0],
            -item[1],
            item[2].submission_name,
            item[2].submission_id,
        )
    )
    return [entry for _, _, entry in entries]