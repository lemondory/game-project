using GameServer.Network;

namespace GameServer.Game.Components;

/// <summary>
/// Network session reference for sending packets
/// </summary>
public struct SessionComponent
{
    public ISession Session;

    public SessionComponent(ISession session)
    {
        Session = session;
    }
}
