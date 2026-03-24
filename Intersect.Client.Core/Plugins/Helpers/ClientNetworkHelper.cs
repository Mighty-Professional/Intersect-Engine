using Intersect.Client.Framework.Plugins.Interfaces;
using Intersect.Client.Networking;
using Intersect.Network;
using Intersect.Plugins.Interfaces;

namespace Intersect.Client.Plugins.Helpers;

/// <summary>
/// Implementation if <see cref="IClientNetworkHelper"/>.
/// </summary>
public sealed partial class ClientNetworkHelper : IClientNetworkHelper
{
    private static IClient? Client => Intersect.Client.Networking.Network.Socket.Network as IClient;

    public ClientNetworkHelper(IPacketHelper packetHelper)
    {
        PacketHelper = packetHelper ?? throw new ArgumentNullException(nameof(packetHelper));
        PacketSender = new PluginPacketSender(PacketHelper);
    }
    
    private IPacketHelper PacketHelper { get; }

    /// <inheritdoc />
    public bool IsConnected => Client?.IsConnected ?? false;

    /// <inheritdoc />
    public bool IsServerOnline => Client?.IsServerOnline ?? false;

    /// <inheritdoc />
    public IPacketSender PacketSender { get; }

    /// <inheritdoc />
    public ConnectionStatistics Statistics => Client?.Connection?.Statistics;
}
