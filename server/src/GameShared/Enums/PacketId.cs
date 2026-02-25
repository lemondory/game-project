namespace GameShared.Enums;

public enum PacketId : ushort
{
    // Login
    C2S_Login = 1000,
    S2C_LoginResult = 1001,

    // Zone Entry
    C2S_EnterTown = 1100,
    S2C_EnterTownResult = 1101,
    C2S_EnterDungeon = 1110,
    S2C_EnterDungeonResult = 1111,
    S2C_LeaveDungeon = 1112,

    // Movement
    C2S_Move = 2000,
    S2C_Move = 2001,
    S2C_Spawn = 2010,
    S2C_Despawn = 2011,

    // Chat
    C2S_Chat = 3000,
    S2C_Chat = 3001,

    // Combat
    C2S_Attack = 4000,
    S2C_Attack = 4001,
    S2C_Damage = 4002,
    S2C_Death = 4003,
}
