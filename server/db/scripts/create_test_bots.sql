-- 성능 테스트용 봇 계정 20개 생성 (bot01 ~ bot20)
-- 실행: psql -U gameuser -d game_auth -f create_test_bots.sql
-- password_hash 는 BCrypt 미적용 더미값 (서버가 단순 비교 통과)

INSERT INTO accounts (username, email, password_hash) VALUES
  ('bot01', 'bot01@test.local', '$2a$11$dummy_hash_for_testing'),
  ('bot02', 'bot02@test.local', '$2a$11$dummy_hash_for_testing'),
  ('bot03', 'bot03@test.local', '$2a$11$dummy_hash_for_testing'),
  ('bot04', 'bot04@test.local', '$2a$11$dummy_hash_for_testing'),
  ('bot05', 'bot05@test.local', '$2a$11$dummy_hash_for_testing'),
  ('bot06', 'bot06@test.local', '$2a$11$dummy_hash_for_testing'),
  ('bot07', 'bot07@test.local', '$2a$11$dummy_hash_for_testing'),
  ('bot08', 'bot08@test.local', '$2a$11$dummy_hash_for_testing'),
  ('bot09', 'bot09@test.local', '$2a$11$dummy_hash_for_testing'),
  ('bot10', 'bot10@test.local', '$2a$11$dummy_hash_for_testing'),
  ('bot11', 'bot11@test.local', '$2a$11$dummy_hash_for_testing'),
  ('bot12', 'bot12@test.local', '$2a$11$dummy_hash_for_testing'),
  ('bot13', 'bot13@test.local', '$2a$11$dummy_hash_for_testing'),
  ('bot14', 'bot14@test.local', '$2a$11$dummy_hash_for_testing'),
  ('bot15', 'bot15@test.local', '$2a$11$dummy_hash_for_testing'),
  ('bot16', 'bot16@test.local', '$2a$11$dummy_hash_for_testing'),
  ('bot17', 'bot17@test.local', '$2a$11$dummy_hash_for_testing'),
  ('bot18', 'bot18@test.local', '$2a$11$dummy_hash_for_testing'),
  ('bot19', 'bot19@test.local', '$2a$11$dummy_hash_for_testing'),
  ('bot20', 'bot20@test.local', '$2a$11$dummy_hash_for_testing')
ON CONFLICT (username) DO NOTHING;

-- common DB 에도 character 생성 필요 (게임서버가 자동 생성하므로 별도 불필요)
SELECT username FROM accounts WHERE username LIKE 'bot%' ORDER BY username;
