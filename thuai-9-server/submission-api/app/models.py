from datetime import datetime

from sqlalchemy import BigInteger, Boolean, DateTime, ForeignKey, Index, Integer, String, Text, UniqueConstraint, func, text
from sqlalchemy.orm import Mapped, mapped_column, relationship

from app.database import Base


class Team(Base):
    __tablename__ = "teams"

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    name: Mapped[str] = mapped_column(String(64), unique=True, nullable=False)
    email: Mapped[str] = mapped_column(String(255), unique=True, nullable=False)
    password_hash: Mapped[str] = mapped_column(String(255), nullable=False)
    game_token: Mapped[str] = mapped_column(String(64), unique=True, nullable=False)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now())

    submissions: Mapped[list["Submission"]] = relationship(back_populates="team")


class Submission(Base):
    __tablename__ = "submissions"

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    team_id: Mapped[int] = mapped_column(Integer, ForeignKey("teams.id", ondelete="CASCADE"), nullable=False)
    name: Mapped[str] = mapped_column(String(64), nullable=False, default="未命名代码")
    language: Mapped[str] = mapped_column(String(8), nullable=False, default="docker", server_default="docker")
    status: Mapped[str] = mapped_column(String(16), nullable=False, default="pending")
    is_dispatched: Mapped[bool] = mapped_column(
        Boolean,
        nullable=False,
        default=False,
        server_default=text("false"),
    )
    error_log: Mapped[str | None] = mapped_column(Text)
    artifact_path: Mapped[str | None] = mapped_column(Text)
    uploaded_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True),
        nullable=False,
        default=func.now(),
        server_default=func.now(),
    )
    compiled_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))

    team: Mapped["Team"] = relationship(back_populates="submissions")


class Match(Base):
    __tablename__ = "matches"

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    mode: Mapped[str] = mapped_column(String(8), nullable=False)
    submission_a_id: Mapped[int] = mapped_column(Integer, ForeignKey("submissions.id"), nullable=False)
    submission_b_id: Mapped[int] = mapped_column(Integer, ForeignKey("submissions.id"), nullable=False)
    status: Mapped[str] = mapped_column(String(16), nullable=False, default="pending")
    score_a: Mapped[int | None] = mapped_column(BigInteger)
    score_b: Mapped[int | None] = mapped_column(BigInteger)
    error_log: Mapped[str | None] = mapped_column(Text)
    scheduled_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now())
    finished_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))


class MatchParticipant(Base):
    __tablename__ = "match_participants"

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    match_id: Mapped[int] = mapped_column(Integer, ForeignKey("matches.id", ondelete="CASCADE"), nullable=False)
    submission_id: Mapped[int] = mapped_column(Integer, ForeignKey("submissions.id"), nullable=False)
    team_id: Mapped[int] = mapped_column(Integer, ForeignKey("teams.id", ondelete="CASCADE"), nullable=False)
    player_token: Mapped[str] = mapped_column(String(64), nullable=False)
    score: Mapped[int | None] = mapped_column(BigInteger)


class SubmissionMatchLog(Base):
    """Per-match agent container stdout/stderr, captured by the evaluator after
    every run regardless of outcome. Visible only to the owning team."""

    __tablename__ = "submission_match_logs"
    # The owning team reads its logs newest-first; this index covers the
    # `WHERE submission_id = ? ORDER BY created_at DESC` access pattern.
    __table_args__ = (
        Index("ix_submission_match_logs_submission_created", "submission_id", "created_at"),
    )

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    match_id: Mapped[int] = mapped_column(Integer, ForeignKey("matches.id", ondelete="CASCADE"), nullable=False)
    submission_id: Mapped[int] = mapped_column(Integer, ForeignKey("submissions.id", ondelete="CASCADE"), nullable=False)
    team_id: Mapped[int] = mapped_column(Integer, ForeignKey("teams.id", ondelete="CASCADE"), nullable=False)
    log: Mapped[str] = mapped_column(Text, nullable=False, default="")
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now())


class Competition(Base):
    __tablename__ = "competitions"
    __table_args__ = (
        Index("ix_competitions_status_scheduled", "status", "scheduled_at"),
    )

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    name: Mapped[str] = mapped_column(String(128), nullable=False)
    description: Mapped[str | None] = mapped_column(Text)
    status: Mapped[str] = mapped_column(String(16), nullable=False, default="scheduled", server_default="scheduled")
    scheduled_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    submission_deadline: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    live_server_image: Mapped[str] = mapped_column(Text, nullable=False)
    created_by_email: Mapped[str] = mapped_column(String(255), nullable=False)
    created_by_name: Mapped[str] = mapped_column(String(64), nullable=False)
    match_id: Mapped[int | None] = mapped_column(Integer, ForeignKey("matches.id", ondelete="SET NULL"))
    error_log: Mapped[str | None] = mapped_column(Text)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now())
    finished_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))


class CompetitionSlot(Base):
    __tablename__ = "competition_slots"
    __table_args__ = (
        UniqueConstraint("competition_id", "team_id", name="uq_competition_slots_competition_team"),
        Index("ix_competition_slots_competition", "competition_id"),
        Index("ix_competition_slots_team", "team_id"),
    )

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    competition_id: Mapped[int] = mapped_column(Integer, ForeignKey("competitions.id", ondelete="CASCADE"), nullable=False)
    team_id: Mapped[int] = mapped_column(Integer, ForeignKey("teams.id", ondelete="CASCADE"), nullable=False)
    selected_submission_id: Mapped[int | None] = mapped_column(
        Integer,
        ForeignKey("submissions.id", ondelete="SET NULL"),
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True),
        server_default=func.now(),
        default=func.now(),
        onupdate=func.now(),
    )
