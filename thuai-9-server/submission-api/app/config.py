from pydantic import field_validator
from pydantic_settings import BaseSettings

# HS256 tokens are only as strong as the signing key; a blank or trivially short
# secret lets anyone forge admin tokens, so we refuse to boot with one.
MIN_JWT_SECRET_CHARS = 16


class Settings(BaseSettings):
    database_url: str
    jwt_secret: str
    admin_email: str
    admin_name: str
    admin_password: str
    upload_dir: str = "/data/uploads"
    artifact_dir: str = "/data/artifacts"
    live_server_data_dir: str = "/workspace/data"
    jwt_algorithm: str = "HS256"
    jwt_expire_hours: int = 24

    class Config:
        env_file = ".env"

    @field_validator("jwt_secret")
    @classmethod
    def jwt_secret_strength(cls, v: str) -> str:
        if len(v.strip()) < MIN_JWT_SECRET_CHARS:
            raise ValueError(
                f"JWT_SECRET 必须至少 {MIN_JWT_SECRET_CHARS} 个字符且不能为空"
                "（生成方法：openssl rand -hex 32）"
            )
        return v

    @field_validator("admin_email")
    @classmethod
    def normalize_admin_email(cls, v: str) -> str:
        text = v.strip().lower()
        if not text:
            raise ValueError("管理员账号配置不能为空")
        return text

    @field_validator("admin_name", "admin_password")
    @classmethod
    def non_empty_text(cls, v: str) -> str:
        text = v.strip()
        if not text:
            raise ValueError("管理员账号配置不能为空")
        return text


settings = Settings()
