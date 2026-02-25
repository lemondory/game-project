-- Common Database Schema
-- 전체 서버 공유 - 서버 목록, 캐릭터 요약

-- World servers - 월드 서버 목록
CREATE TABLE world_servers (
    server_id SERIAL PRIMARY KEY,
    server_name VARCHAR(100) UNIQUE NOT NULL,
    server_type VARCHAR(50) NOT NULL, -- 'pvp', 'pve', 'rp'
    region VARCHAR(50) NOT NULL, -- 'kr', 'us', 'eu', 'jp'
    host VARCHAR(255) NOT NULL,
    port INT NOT NULL,
    max_players INT DEFAULT 5000,
    current_players INT DEFAULT 0,
    status VARCHAR(50) DEFAULT 'online', -- 'online', 'maintenance', 'offline'
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Character summaries - 캐릭터 요약 정보 (서버 선택 화면용)
CREATE TABLE character_summaries (
    character_id BIGSERIAL PRIMARY KEY,
    account_id BIGINT NOT NULL,
    server_id INT NOT NULL REFERENCES world_servers(server_id),
    character_name VARCHAR(50) NOT NULL,
    character_level INT DEFAULT 1,
    character_class VARCHAR(50),
    last_played_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    is_deleted BOOLEAN DEFAULT FALSE,
    deleted_at TIMESTAMP,
    UNIQUE(server_id, character_name)
);

-- Server status log - 서버 상태 이력
CREATE TABLE server_status_log (
    log_id BIGSERIAL PRIMARY KEY,
    server_id INT NOT NULL REFERENCES world_servers(server_id),
    status VARCHAR(50) NOT NULL,
    player_count INT,
    logged_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Indexes
CREATE INDEX idx_character_summaries_account_id ON character_summaries(account_id);
CREATE INDEX idx_character_summaries_server_id ON character_summaries(server_id);
CREATE INDEX idx_character_summaries_name ON character_summaries(character_name);
CREATE INDEX idx_server_status_log_server_id ON server_status_log(server_id);

-- Sample data
INSERT INTO world_servers (server_name, server_type, region, host, port, max_players) VALUES
('Seoul-1', 'pve', 'kr', 'localhost', 7777, 5000),
('Seoul-2', 'pvp', 'kr', 'localhost', 7778, 5000),
('Tokyo-1', 'pve', 'jp', 'localhost', 7779, 3000);

-- Sample characters
INSERT INTO character_summaries (account_id, server_id, character_name, character_level, character_class) VALUES
(1, 1, 'TestHero1', 10, 'Warrior'),
(1, 2, 'TestMage1', 5, 'Mage'),
(2, 1, 'TestArcher2', 15, 'Archer'),
(3, 1, 'AliceChar', 1, 'Warrior'),
(4, 1, 'BobChar', 1, 'Mage');
