namespace GameServer.Database.Models;

/// <summary>
/// Game DB — player_field_quota 테이블
/// 플레이어별 시간제 사냥터 일간/주간 쿼터
/// </summary>
public class PlayerFieldQuota
{
    public long     QuotaId            { get; set; }
    public long     PlayerId           { get; set; }
    public int      FieldId            { get; set; }
    public int      DailyUsedSeconds   { get; set; }
    public int      WeeklyUsedSeconds  { get; set; }
    public DateTime LastDailyReset     { get; set; }
    public DateTime LastWeeklyReset    { get; set; }
    public DateTime? LastEnteredAt     { get; set; }
    public DateTime  LastSavedAt       { get; set; }
}
