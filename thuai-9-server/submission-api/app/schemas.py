from datetime import datetime
import re

from pydantic import BaseModel, field_validator


EMAIL_RE = re.compile(r"^[^@\s]+@[^@\s]+\.[^@\s]+$")


class TeamRegister(BaseModel):
    name: str
    email: str
    password: str

    @field_validator("name")
    @classmethod
    def name_length(cls, v: str) -> str:
        v = v.strip()
        if not 2 <= len(v) <= 64:
            raise ValueError("队伍名称长度须在 2-64 字符之间")
        return v

    @field_validator("email")
    @classmethod
    def email_format(cls, v: str) -> str:
        v = v.strip().lower()
        if not EMAIL_RE.match(v):
            raise ValueError("邮箱格式不正确")
        return v

    @field_validator("password")
    @classmethod
    def password_length(cls, v: str) -> str:
        if len(v) < 8:
            raise ValueError("密码至少 8 位")
        return v


class TeamLogin(BaseModel):
    email: str
    password: str

    @field_validator("email")
    @classmethod
    def email_format(cls, v: str) -> str:
        v = v.strip().lower()
        if not EMAIL_RE.match(v):
            raise ValueError("邮箱格式不正确")
        return v


class TokenResponse(BaseModel):
    access_token: str
    token_type: str = "bearer"
    game_token: str
    id: int | None = None
    role: str | None = None
    display_name: str | None = None
    email: str | None = None


class SubmissionOut(BaseModel):
    id: int
    name: str
    language: str
    status: str
    is_dispatched: bool
    error_log: str | None
    uploaded_at: datetime | None
    compiled_at: datetime | None

    model_config = {"from_attributes": True}


class MatchOut(BaseModel):
    id: int
    mode: str
    submission_a_id: int
    submission_b_id: int
    status: str
    score_a: str | None
    score_b: str | None
    scheduled_at: datetime
    finished_at: datetime | None


class TriggerMatchRequest(BaseModel):
    submission_a_id: int
    submission_b_id: int


class LeaderboardEntry(BaseModel):
    submission_id: int
    submission_name: str
    team_name: str
    total_score: str
    average_score: str
    best_score: str | None
    total_matches: int


class SubmissionMatchLogEntry(BaseModel):
    match_id: int
    status: str
    score: str | None
    scheduled_at: datetime
    finished_at: datetime | None
    log: str


class SubmissionLogsOut(BaseModel):
    submission_id: int
    status: str
    compile_log: str | None
    matches: list[SubmissionMatchLogEntry]


class PlayerMapEntry(BaseModel):
    # player_id is the game server's 0-based player index for this match; it
    # matches the playerId in the live GAME_STATE stream. The secret player_token
    # is intentionally NOT exposed here.
    player_id: int
    team_id: int
    team_name: str


class PlayerMapOut(BaseModel):
    match_id: int | None
    status: str | None
    players: list[PlayerMapEntry]


class LivePlayerStateOut(BaseModel):
    player_id: int
    team_id: int
    team_name: str
    submission_id: int | None
    submission_name: str | None
    player_name: str | None
    connected: bool
    in_game: bool
    score: str | None
    current_nav: str | None
    mora: str | None
    frozen_mora: str | None
    gold: int | None
    frozen_gold: int | None
    locked_gold: int | None
    monthly_trade_count: int | None
    active_cards: list[str]


class LiveMatchStateOut(BaseModel):
    match_id: int | None
    status: str | None
    stage: str | None
    current_month: int | None
    current_day: int | None
    current_tick: int | None
    players: list[LivePlayerStateOut]


class CurrentUserOut(BaseModel):
    role: str
    id: int | None
    display_name: str
    email: str
    game_token: str


class TeamAccountOut(BaseModel):
    id: int
    name: str
    email: str


class CompetitionCreateRequest(BaseModel):
    name: str
    description: str | None = None
    scheduled_at: datetime
    submission_deadline: datetime | None = None
    live_server_image: str
    eligible_team_ids: list[int]

    @field_validator("name", "live_server_image")
    @classmethod
    def required_text(cls, v: str) -> str:
        text = v.strip()
        if not text:
            raise ValueError("字段不能为空")
        return text

    @field_validator("eligible_team_ids")
    @classmethod
    def non_empty_team_ids(cls, v: list[int]) -> list[int]:
        deduped = list(dict.fromkeys(v))
        if not deduped:
            raise ValueError("至少选择一个参赛账号")
        return deduped


class CompetitionEnrollRequest(BaseModel):
    submission_id: int


class CompetitionSlotOut(BaseModel):
    team_id: int
    team_name: str
    selected_submission_id: int | None
    selected_submission_name: str | None
    selected_submission_status: str | None
    updated_at: datetime
    score: str | None


class CompetitionSummaryOut(BaseModel):
    id: int
    name: str
    description: str | None
    status: str
    scheduled_at: datetime
    submission_deadline: datetime | None
    effective_deadline: datetime
    live_server_image: str
    created_by_name: str
    created_by_email: str
    match_id: int | None
    eligible_team_count: int
    participant_count: int
    is_eligible: bool
    current_submission_id: int | None


class CompetitionDetailOut(CompetitionSummaryOut):
    error_log: str | None
    finished_at: datetime | None
    slots: list[CompetitionSlotOut]
