namespace GameServer.Database;

/// <summary>
/// Database connection configuration
/// </summary>
public class DatabaseConfig
{
    public string AuthConnectionString { get; set; } = string.Empty;
    public string CommonConnectionString { get; set; } = string.Empty;
    public string GameConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 환경변수에서 DB 연결 문자열을 읽는다.
    /// 환경변수가 없으면 로컬 개발용 docker-compose 기본값을 사용한다.
    /// 프로덕션 환경에서는 반드시 환경변수를 설정할 것 (.env.example 참고)
    /// </summary>
    public static DatabaseConfig FromEnvironment()
    {
        const string devDefault = "dev_password_123"; // docker-compose.yml 의 POSTGRES_PASSWORD 와 일치

        return new DatabaseConfig
        {
            AuthConnectionString   = Environment.GetEnvironmentVariable("DB_AUTH_CONNECTION")
                ?? $"Host=localhost;Port=5435;Database=auth;Username=gameserver;Password={devDefault}",
            CommonConnectionString = Environment.GetEnvironmentVariable("DB_COMMON_CONNECTION")
                ?? $"Host=localhost;Port=5433;Database=common;Username=gameserver;Password={devDefault}",
            GameConnectionString   = Environment.GetEnvironmentVariable("DB_GAME_CONNECTION")
                ?? $"Host=localhost;Port=5434;Database=game;Username=gameserver;Password={devDefault}"
        };
    }
}
