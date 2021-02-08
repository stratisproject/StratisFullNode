using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Protocol;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Configuration.Settings;
using Stratis.Bitcoin.EventBus.CoreEvents;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.Extensions;

namespace Stratis.Bitcoin.P2P.Peer
{
    public class NetworkPeerServer : IDisposable
    {
        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>Factory for creating P2P network peers.</summary>
        private readonly INetworkPeerFactory networkPeerFactory;

        /// <summary>Specification of the network the node runs on - regtest/testnet/mainnet.</summary>
        public Network Network { get; private set; }

        /// <summary>Version of the protocol that the server is running.</summary>
        public ProtocolVersion Version { get; private set; }

        /// <summary>The parameters that will be cloned and applied for each peer connecting to <see cref="NetworkPeerServer"/>.</summary>
        public NetworkPeerConnectionParameters InboundNetworkPeerConnectionParameters { get; set; }

        /// <summary>IP address and port, on which the server listens to incoming connections.</summary>
        public IPEndPoint LocalEndpoint { get; private set; }

        /// <summary>IP address and port of the external network interface that is accessible from the Internet.</summary>
        public IPEndPoint ExternalEndpoint { get; private set; }

        /// <summary>TCP server listener accepting inbound connections.</summary>
        private readonly TcpListener tcpListener;

        /// <summary>Cancellation that is triggered on shutdown to stop all pending operations.</summary>
        private readonly CancellationTokenSource serverCancel;

        /// <summary>Maintains a list of connected peers and ensures their proper disposal.</summary>
        private readonly NetworkPeerDisposer networkPeerDisposer;

        /// <summary> The number connected inbound peers that the disposer has to dispose of.</summary>
        public int? ConnectedInboundPeersCount { get { return this.networkPeerDisposer?.ConnectedInboundPeersCount; } }

        /// <summary>Task accepting new clients in a loop.</summary>
        private Task acceptTask;

        /// <summary>Provider of IBD state.</summary>
        private readonly IInitialBlockDownloadState initialBlockDownloadState;

        /// <summary>Configuration related to incoming and outgoing connections.</summary>
        private readonly ConnectionManagerSettings connectionManagerSettings;

        private readonly IPeerAddressManager peerAddressManager;

        private readonly IDateTimeProvider dateTimeProvider;

        /// <summary>Used to publish application events.</summary>
        private readonly ISignals signals;

        /// <summary>
        /// Initializes instance of a network peer server.
        /// </summary>
        /// <param name="network">Specification of the network the node runs on - regtest/testnet/mainnet.</param>
        /// <param name="localEndPoint">IP address and port to listen on.</param>
        /// <param name="externalEndPoint">IP address and port that the server is reachable from the Internet on.</param>
        /// <param name="version">Version of the network protocol that the server should run.</param>
        /// <param name="loggerFactory">Factory for creating loggers.</param>
        /// <param name="networkPeerFactory">Factory for creating P2P network peers.</param>
        /// <param name="initialBlockDownloadState">Provider of IBD state.</param>
        /// <param name="connectionManagerSettings">Configuration related to incoming and outgoing connections.</param>
        public NetworkPeerServer(Network network,
            IPEndPoint localEndPoint,
            IPEndPoint externalEndPoint,
            ProtocolVersion version,
            ILoggerFactory loggerFactory,
            INetworkPeerFactory networkPeerFactory,
            IInitialBlockDownloadState initialBlockDownloadState,
            ConnectionManagerSettings connectionManagerSettings,
            IAsyncProvider asyncProvider,
            IPeerAddressManager peerAddressManager,
            IDateTimeProvider dateTimeProvider)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName, $"[{localEndPoint}] ");
            this.signals = asyncProvider.Signals;
            this.networkPeerFactory = networkPeerFactory;
            this.networkPeerDisposer = new NetworkPeerDisposer(loggerFactory, asyncProvider);
            this.initialBlockDownloadState = initialBlockDownloadState;
            this.connectionManagerSettings = connectionManagerSettings;
            this.peerAddressManager = peerAddressManager;
            this.dateTimeProvider = dateTimeProvider;

            this.InboundNetworkPeerConnectionParameters = new NetworkPeerConnectionParameters();

            this.LocalEndpoint = Utils.EnsureIPv6(localEndPoint);
            this.ExternalEndpoint = Utils.EnsureIPv6(externalEndPoint);

            this.Network = network;
            this.Version = version;

            this.serverCancel = new CancellationTokenSource();

            this.tcpListener = new TcpListener(this.LocalEndpoint);
            this.tcpListener.Server.LingerState = new LingerOption(true, 0);
            this.tcpListener.Server.NoDelay = true;
            this.tcpListener.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);

            this.acceptTask = Task.CompletedTask;

            this.logger.LogDebug("Network peer server ready to listen on '{0}'.", this.LocalEndpoint);
        }

        /// <summary>
        /// Starts listening on the server's initialized endpoint.
        /// </summary>
        public void Listen(IReadOnlyNetworkPeerCollection networkPeers, List<IPEndPoint> iprangeFilteringExclusions)
        {
            try
            {
                this.tcpListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                this.tcpListener.Start();
                this.acceptTask = this.AcceptClientsAsync(networkPeers, iprangeFilteringExclusions);
            }
            catch (Exception e)
            {
                this.logger.LogDebug("Exception occurred: {0}", e.ToString());
                throw;
            }
        }

        /// <summary>
        /// Implements loop accepting connections from newly connected clients.
        /// </summary>
        private async Task AcceptClientsAsync(IReadOnlyNetworkPeerCollection networkPeers, List<IPEndPoint> iprangeFilteringExclusions)
        {
            this.logger.LogDebug("Accepting incoming connections.");

            try
            {
                while (!this.serverCancel.IsCancellationRequested)
                {
                    TcpClient tcpClient = await this.tcpListener.AcceptTcpClientAsync().WithCancellationAsync(this.serverCancel.Token).ConfigureAwait(false);

                    (bool successful, string reason) = this.AllowClientConnection(tcpClient, networkPeers, iprangeFilteringExclusions);
                    if (!successful)
                    {
                        this.signals.Publish(new PeerConnectionAttemptFailed(true, (IPEndPoint)tcpClient.Client.RemoteEndPoint, reason));
                        this.logger.LogDebug("Connection from client '{0}' was rejected and will be closed, reason: {1}", tcpClient.Client.RemoteEndPoint, reason);
                        tcpClient.Close();
                        continue;
                    }

                    this.logger.LogDebug("Connection accepted from client '{0}'.", tcpClient.Client.RemoteEndPoint);

                    INetworkPeer connectedPeer = this.networkPeerFactory.CreateNetworkPeer(tcpClient, this.CreateNetworkPeerConnectionParameters(), this.networkPeerDisposer);
                    this.signals.Publish(new PeerConnected(connectedPeer.Inbound, connectedPeer.PeerEndPoint));
                }
            }
            catch (OperationCanceledException)
            {
                this.logger.LogDebug("Shutdown detected, stop accepting connections.");
            }
            catch (Exception e)
            {
                this.logger.LogDebug("Exception occurred: {0}", e.ToString());
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.serverCancel.Cancel();

            this.logger.LogDebug("Stopping TCP listener.");
            this.tcpListener.Stop();

            this.logger.LogDebug("Waiting for accepting task to complete.");
            this.acceptTask.Wait();

            if (this.networkPeerDisposer.ConnectedPeersCount > 0)
                this.logger.LogInformation("Waiting for {0} connected clients to dispose...", this.networkPeerDisposer.ConnectedPeersCount);

            this.networkPeerDisposer.Dispose();
        }

        /// <summary>
        /// Initializes connection parameters using the server's initialized values.
        /// </summary>
        /// <returns>Initialized connection parameters.</returns>
        private NetworkPeerConnectionParameters CreateNetworkPeerConnectionParameters()
        {
            IPEndPoint myExternal = this.ExternalEndpoint;
            NetworkPeerConnectionParameters param2 = this.InboundNetworkPeerConnectionParameters.Clone();
            param2.Version = this.Version;
            param2.AddressFrom = myExternal;
            return param2;
        }

        /// <summary>
        /// Check if the client is allowed to connect based on certain criteria.
        /// </summary>
        /// <returns>When criteria is met returns <c>true</c>, to allow connection.</returns>
        private (bool successful, string reason) AllowClientConnection(TcpClient tcpClient, IReadOnlyNetworkPeerCollection networkPeers, List<IPEndPoint> iprangeFilteringExclusions)
        {
            // This is the IP address of the client connection.
            var clientRemoteEndPoint = tcpClient.Client.RemoteEndPoint as IPEndPoint;

            var isFromSameNetworkGroup = this.CheckIfPeerFromSameNetworkGroup(networkPeers, clientRemoteEndPoint, iprangeFilteringExclusions);
            if (isFromSameNetworkGroup)
            {
                this.logger.LogTrace("(-)[PEER_SAME_NETWORK_GROUP]:false");
                return (false, $"Inbound Refused: Peer {clientRemoteEndPoint} is from the same network group.");
            }

            var peers = this.peerAddressManager.FindPeersByIp(clientRemoteEndPoint);
            var bannedPeer = peers.FirstOrDefault(p => p.IsBanned(this.dateTimeProvider.GetUtcNow()));
            if (bannedPeer != null)
            {
                this.logger.LogTrace("(-)[PEER_BANNED]:false");
                return (false, $"Inbound Refused: Peer {clientRemoteEndPoint} is banned until {bannedPeer.BanUntil}.");
            }

            if (this.networkPeerDisposer.ConnectedInboundPeersCount >= this.connectionManagerSettings.MaxInboundConnections)
            {
                this.logger.LogTrace("(-)[MAX_CONNECTION_THRESHOLD_REACHED]:false");
                return (false, $"Inbound Refused: Max Inbound Connection Threshold Reached, inbounds: {this.networkPeerDisposer.ConnectedInboundPeersCount}");
            }

            if (!this.initialBlockDownloadState.IsInitialBlockDownload())
            {
                this.logger.LogTrace("(-)[IBD_COMPLETE_ALLOW_CONNECTION]:true");
                return (true, "Inbound Accepted: IBD Complete.");
            }

            // This is the network interface the client connection is being made against, not the IP of the client itself.
            var clientLocalEndPoint = tcpClient.Client.LocalEndPoint as IPEndPoint;

            // This checks whether the network interface being connected to by the client is configured to whitelist all inbound connections (i.e. -whitebind).
            bool endpointCanBeWhiteListed = this.connectionManagerSettings.Bind.Where(x => x.Whitelisted).Any(x => x.Endpoint.MapToIpv6().Contains(clientLocalEndPoint));

            if (endpointCanBeWhiteListed)
            {
                this.logger.LogTrace("(-)[ENDPOINT_WHITELISTED_ALLOW_CONNECTION]:true");
                return (true, "Inbound Accepted: Whitelisted endpoint connected during IBD.");
            }

            // This checks whether the client IP itself has been whitelisted (i.e. -whitelist).
            bool clientEndpointCanBeWhiteListed = this.connectionManagerSettings.Whitelist.Any(x => x.MatchIpOnly(clientRemoteEndPoint));

            if (clientEndpointCanBeWhiteListed)
            {
                this.logger.LogTrace("(-)[CLIENT_WHITELISTED_ALLOW_CONNECTION]:true");
                return (true, "Inbound Accepted: Whitelisted client connected during IBD.");
            }

            this.logger.LogInformation("Node '{0}' is not whitelisted via endpoint '{1}' during initial block download.", clientRemoteEndPoint, clientLocalEndPoint);

            return (false, "Inbound Refused: Non Whitelisted endpoint connected during IBD.");
        }

        /// <summary>
        /// Determines if the peer should be disconnected.
        /// Peer should be disconnected in case it's IP is from the same group in which any other peer
        /// is and the peer wasn't added using -connect or -addNode command line arguments.
        /// </summary>
        private bool CheckIfPeerFromSameNetworkGroup(IReadOnlyNetworkPeerCollection networkPeers, IPEndPoint ipEndpoint, List<IPEndPoint> iprangeFilteringExclusions)
        {
            // Don't disconnect if range filtering is not turned on.
            if (!this.connectionManagerSettings.IpRangeFiltering)
            {
                this.logger.LogTrace("(-)[IP_RANGE_FILTERING_OFF]:false");
                return false;
            }

            // Don't disconnect if this peer has a local host address.
            if (ipEndpoint.Address.IsLocal())
            {
                this.logger.LogTrace("(-)[IP_IS_LOCAL]:false");
                return false;
            }

            // Don't disconnect if this peer is in -addnode or -connect.
            if (this.connectionManagerSettings.RetrieveAddNodes().Union(this.connectionManagerSettings.Connect).Any(ep => ipEndpoint.MatchIpOnly(ep)))
            {
                this.logger.LogTrace("(-)[ADD_NODE_OR_CONNECT]:false");
                return false;
            }

            // Don't disconnect if this peer is in the exclude from IP range filtering group.
            if (iprangeFilteringExclusions.Any(ip => ip.MatchIpOnly(ipEndpoint)))
            {
                this.logger.LogTrace("(-)[PEER_IN_IPRANGEFILTER_EXCLUSIONS]:false");
                return false;
            }

            byte[] peerGroup = ipEndpoint.MapToIpv6().Address.GetGroup();

            foreach (INetworkPeer connectedPeer in networkPeers)
            {
                if (ipEndpoint == connectedPeer.PeerEndPoint)
                    continue;

                byte[] group = connectedPeer.PeerEndPoint.MapToIpv6().Address.GetGroup();

                if (peerGroup.SequenceEqual(group))
                {
                    this.logger.LogTrace("(-)[SAME_GROUP]:true");
                    return true;
                }
            }

            return false;
        }
    }
}