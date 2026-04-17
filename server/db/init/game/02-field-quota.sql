-- 시간제 사냥터 쿼터 테이블
-- 플레이어별 일간/주간 입장 가능 시간을 관리한다.
-- daily_used_seconds  : 오늘 사용한 초 (UTC 자정에 리셋)
-- weekly_used_seconds : 이번 주 사용한 초 (UTC 월요일 자정에 리셋)

CREATE TABLE player_field_quota (
    quota_id             BIGSERIAL PRIMARY KEY,
    player_id            BIGINT NOT NULL REFERENCES players(player_id) ON DELETE CASCADE,
    field_id             INT NOT NULL,
    daily_used_seconds   INT NOT NULL DEFAULT 0,
    weekly_used_seconds  INT NOT NULL DEFAULT 0,
    last_daily_reset     DATE NOT NULL DEFAULT CURRENT_DATE,
    last_weekly_reset    DATE NOT NULL DEFAULT DATE_TRUNC('week', CURRENT_DATE)::DATE,
    last_entered_at      TIMESTAMP,
    last_saved_at        TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(player_id, field_id)
);

CREATE INDEX idx_field_quota_player ON player_field_quota(player_id);
