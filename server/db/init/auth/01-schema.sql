-- Auth Database Schema
-- 전체 서버 공유 - 계정 인증 정보

-- Accounts table - 계정 정보
CREATE TABLE accounts (
    account_id BIGSERIAL PRIMARY KEY,
    username VARCHAR(50) UNIQUE NOT NULL,
    email VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_login_at TIMESTAMP,
    is_active BOOLEAN DEFAULT TRUE,
    is_banned BOOLEAN DEFAULT FALSE,
    ban_reason TEXT,
    ban_until TIMESTAMP
);

-- OAuth connections - OAuth 연동 정보
CREATE TABLE oauth_connections (
    oauth_id BIGSERIAL PRIMARY KEY,
    account_id BIGINT NOT NULL REFERENCES accounts(account_id) ON DELETE CASCADE,
    provider VARCHAR(50) NOT NULL, -- 'google', 'apple', 'steam'
    provider_user_id VARCHAR(255) NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(provider, provider_user_id)
);

-- Login sessions - 로그인 세션 (JWT 관리)
CREATE TABLE login_sessions (
    session_id BIGSERIAL PRIMARY KEY,
    account_id BIGINT NOT NULL REFERENCES accounts(account_id) ON DELETE CASCADE,
    jwt_token VARCHAR(512) NOT NULL,
    refresh_token VARCHAR(512),
    ip_address INET,
    user_agent TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    expires_at TIMESTAMP NOT NULL,
    revoked_at TIMESTAMP
);

-- Indexes
CREATE INDEX idx_accounts_username ON accounts(username);
CREATE INDEX idx_accounts_email ON accounts(email);
CREATE INDEX idx_oauth_account_id ON oauth_connections(account_id);
CREATE INDEX idx_login_sessions_account_id ON login_sessions(account_id);
CREATE INDEX idx_login_sessions_jwt_token ON login_sessions(jwt_token);

-- Sample data for testing
INSERT INTO accounts (username, email, password_hash) VALUES
('testuser1', 'test1@example.com', '$2a$11$dummy_hash_for_testing'),
('testuser2', 'test2@example.com', '$2a$11$dummy_hash_for_testing'),
('alice', 'alice@example.com', '$2a$11$dummy_hash_for_testing'),
('bob', 'bob@example.com', '$2a$11$dummy_hash_for_testing');
