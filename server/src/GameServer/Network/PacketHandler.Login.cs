using GameServer.Database;
using GameShared.Enums;
using GameShared.Proto;
using Serilog;

namespace GameServer.Network;

public partial class PacketHandler
{
    [PacketHandler(PacketId.C2S_Login)]
    private void OnLogin(ISession session, C2S_Login packet)
    {
        FireAndForget(OnLoginAsync(session, packet));
    }

    private async Task OnLoginAsync(ISession session, C2S_Login packet)
    {
        Log.Information("Session {Id}: login request — {Username}", session.SessionId, packet.Username);

        try
        {
            var account = await DatabaseManager.Instance.Auth.GetAccountByUsernameAsync(packet.Username);

            if (account == null)
            {
                session.Send(PacketId.S2C_LoginResult, new S2C_LoginResult
                    { Success = false, Message = "Invalid username or password" });
                return;
            }

            if (account.IsBanned)
            {
                var msg = account.BanUntil.HasValue
                    ? $"Account banned until {account.BanUntil.Value:yyyy-MM-dd HH:mm}"
                    : "Account permanently banned";
                session.Send(PacketId.S2C_LoginResult, new S2C_LoginResult { Success = false, Message = msg });
                return;
            }

            // TODO: BCrypt 패스워드 검증
            // if (!BCrypt.Net.BCrypt.Verify(packet.Password, account.PasswordHash)) { ... }

            await DatabaseManager.Instance.Auth.UpdateLastLoginAsync(account.AccountId);

            // 중복 로그인 처리: 기존 세션이 있으면 강제 종료
            if (_loggedInSessions.TryGetValue(account.AccountId, out var existingSession))
            {
                Log.Warning("Session {Id}: duplicate login for AccountId={AccountId}, kicking existing session {ExistingId}",
                    session.SessionId, account.AccountId, existingSession.SessionId);

                existingSession.Send(PacketId.S2C_ForceLogout, new S2C_ForceLogout
                    { Message = "다른 곳에서 로그인되었습니다." });
                existingSession.Disconnect();
            }

            session.PlayerId   = account.AccountId;
            session.PlayerName = account.Username;

            _loggedInSessions[account.AccountId] = session;

            session.Send(PacketId.S2C_LoginResult, new S2C_LoginResult
            {
                Success    = true,
                Message    = "Login successful",
                PlayerId   = account.AccountId,
                PlayerName = account.Username
            });

            Log.Information("Session {Id}: login OK — AccountId={AccountId}", session.SessionId, account.AccountId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Session {Id}: login failed", session.SessionId);
            session.Send(PacketId.S2C_LoginResult, new S2C_LoginResult
                { Success = false, Message = "Server error" });
        }
    }
}
