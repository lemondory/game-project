-- Game Database Schema
-- 월드 서버별 독립 - 실제 게임 데이터

-- Players - 플레이어 상세 정보
CREATE TABLE players (
    player_id BIGSERIAL PRIMARY KEY,
    character_id BIGINT UNIQUE NOT NULL, -- common DB의 character_summaries.character_id
    account_id BIGINT NOT NULL, -- auth DB의 accounts.account_id
    character_name VARCHAR(50) UNIQUE NOT NULL,

    -- Stats
    level INT DEFAULT 1,
    experience BIGINT DEFAULT 0,
    character_class VARCHAR(50) DEFAULT 'Warrior',

    -- Position
    zone_type VARCHAR(50) DEFAULT 'Town', -- 'Town', 'Dungeon'
    zone_id INT DEFAULT 1,
    position_x FLOAT DEFAULT 0,
    position_y FLOAT DEFAULT 0,
    position_z FLOAT DEFAULT 0,

    -- Combat stats
    max_hp INT DEFAULT 100,
    current_hp INT DEFAULT 100,
    attack_power INT DEFAULT 10,
    defense INT DEFAULT 5,

    -- Timestamps
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_login_at TIMESTAMP,
    last_logout_at TIMESTAMP,
    total_play_time_seconds BIGINT DEFAULT 0
);

-- Inventory - 인벤토리
CREATE TABLE inventory (
    inventory_id BIGSERIAL PRIMARY KEY,
    player_id BIGINT NOT NULL REFERENCES players(player_id) ON DELETE CASCADE,
    item_id INT NOT NULL,
    item_name VARCHAR(100),
    quantity INT DEFAULT 1,
    slot_index INT,
    acquired_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Quest progress - 퀘스트 진행도
CREATE TABLE quest_progress (
    quest_progress_id BIGSERIAL PRIMARY KEY,
    player_id BIGINT NOT NULL REFERENCES players(player_id) ON DELETE CASCADE,
    quest_id INT NOT NULL,
    status VARCHAR(50) DEFAULT 'in_progress', -- 'in_progress', 'completed', 'failed'
    progress_data JSONB, -- 퀘스트별 진행 상태 (유연한 구조)
    started_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    completed_at TIMESTAMP,
    UNIQUE(player_id, quest_id)
);

-- Player achievements - 업적
CREATE TABLE player_achievements (
    achievement_id BIGSERIAL PRIMARY KEY,
    player_id BIGINT NOT NULL REFERENCES players(player_id) ON DELETE CASCADE,
    achievement_type VARCHAR(100) NOT NULL,
    achieved_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(player_id, achievement_type)
);

-- Mail system - 우편함
CREATE TABLE player_mail (
    mail_id BIGSERIAL PRIMARY KEY,
    receiver_player_id BIGINT NOT NULL REFERENCES players(player_id) ON DELETE CASCADE,
    sender_name VARCHAR(50),
    subject VARCHAR(200),
    body TEXT,
    attached_item_id INT,
    attached_quantity INT,
    is_read BOOLEAN DEFAULT FALSE,
    is_claimed BOOLEAN DEFAULT FALSE,
    sent_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    expires_at TIMESTAMP
);

-- Indexes
CREATE INDEX idx_players_account_id ON players(account_id);
CREATE INDEX idx_players_character_id ON players(character_id);
CREATE INDEX idx_players_character_name ON players(character_name);
CREATE INDEX idx_inventory_player_id ON inventory(player_id);
CREATE INDEX idx_quest_progress_player_id ON quest_progress(player_id);
CREATE INDEX idx_player_mail_receiver ON player_mail(receiver_player_id);

-- Sample data
INSERT INTO players (character_id, account_id, character_name, level, experience, character_class, max_hp, current_hp, attack_power) VALUES
(1, 1, 'TestHero1', 10, 5000, 'Warrior', 150, 150, 20),
(2, 1, 'TestMage1', 5, 1000, 'Mage', 80, 80, 15),
(3, 2, 'TestArcher2', 15, 10000, 'Archer', 120, 120, 25),
(4, 3, 'AliceChar', 1, 0, 'Warrior', 100, 100, 10),
(5, 4, 'BobChar', 1, 0, 'Mage', 100, 100, 10);

-- Sample inventory items
INSERT INTO inventory (player_id, item_id, item_name, quantity, slot_index) VALUES
(1, 1001, 'Health Potion', 10, 0),
(1, 1002, 'Mana Potion', 5, 1),
(2, 1001, 'Health Potion', 3, 0),
(3, 1003, 'Arrow Bundle', 50, 0);
