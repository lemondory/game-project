using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using GameShared.Enums;
using Google.Protobuf;
using Serilog;

namespace GameServer.Network;

public partial class PacketHandler
{
    private const int MaxChatLength = 200;
    private const float MaxCoordinate = 10000f;

    private readonly Dictionary<PacketId, Action<ISession, byte[]>> _handlers = new();
    private readonly Dictionary<long, long> _sessionToEntityId = new();
    private readonly Dictionary<long, int>  _sessionToZoneId   = new();
    private readonly ConcurrentDictionary<long, ISession> _loggedInSessions = new();

    public PacketHandler(TcpServer server)
    {
        server.SessionDisconnected += session =>
        {
            _sessionToEntityId.Remove(session.SessionId);
            _sessionToZoneId.Remove(session.SessionId);

            if (session.PlayerId.HasValue)
            {
                _loggedInSessions.TryRemove(
                    new KeyValuePair<long, ISession>(session.PlayerId.Value, session));
            }
        };

        RegisterHandlers();
    }

    private void RegisterHandlers()
    {
        var registerMethod = GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == "Register" && m.IsGenericMethod);

        var handlers = GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(m => m.GetCustomAttribute<PacketHandlerAttribute>() != null);

        foreach (var method in handlers)
        {
            var attr       = method.GetCustomAttribute<PacketHandlerAttribute>()!;
            var packetType = method.GetParameters()[1].ParameterType;

            var parser = packetType
                .GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;

            var handlerDelegate = Delegate.CreateDelegate(
                typeof(Action<,>).MakeGenericType(typeof(ISession), packetType),
                this,
                method);

            registerMethod.MakeGenericMethod(packetType)
                          .Invoke(this, new[] { attr.PacketId, parser, handlerDelegate });
        }
    }

    private void Register<T>(PacketId packetId, MessageParser<T> parser, Action<ISession, T> handler)
        where T : IMessage<T>
    {
        _handlers[packetId] = (session, data) =>
        {
            try
            {
                handler(session, parser.ParseFrom(data));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PacketHandler: deserialization failed for {PacketId}", packetId);
            }
        };
    }

    public void Handle(ISession session, PacketId packetId, byte[] data)
    {
        if (_handlers.TryGetValue(packetId, out var handler))
            handler(session, data);
        else
            Log.Warning("PacketHandler: no handler for {PacketId}", packetId);
    }

    internal static bool IsValidCoordinate(float x, float y, float z) =>
        MathF.Abs(x) <= MaxCoordinate &&
        MathF.Abs(y) <= MaxCoordinate &&
        MathF.Abs(z) <= MaxCoordinate &&
        !float.IsNaN(x) && !float.IsNaN(y) && !float.IsNaN(z);

    private static void FireAndForget(Task task)
    {
        task.ContinueWith(
            t => Log.Error(t.Exception?.GetBaseException(), "PacketHandler: unhandled async error"),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
