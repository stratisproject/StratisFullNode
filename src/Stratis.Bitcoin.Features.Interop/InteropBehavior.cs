using System.Numerics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Features.Interop.EthereumClient;
using Stratis.Bitcoin.Features.Interop.Payloads;
using Stratis.Bitcoin.P2P.Peer;
using Stratis.Bitcoin.P2P.Protocol;
using Stratis.Bitcoin.P2P.Protocol.Behaviors;
using Stratis.Bitcoin.Utilities;
using TracerAttributes;

namespace Stratis.Bitcoin.Features.Interop
{
    public class InteropBehavior : NetworkPeerBehavior
    {
        private readonly ILoggerFactory loggerFactory;

        private readonly ILogger logger;

        private readonly Network network;

        private readonly IInteropTransactionManager interopTransactionManager;

        private readonly IEthereumClientBase ethereumClientBase;

        public InteropBehavior(
            ILoggerFactory loggerFactory,
            Network network,
            IInteropTransactionManager interopTransactionManager,
            IEthereumClientBase ethereumClientBase)
        {
            Guard.NotNull(loggerFactory, nameof(loggerFactory));
            Guard.NotNull(network, nameof(network));
            Guard.NotNull(interopTransactionManager, nameof(interopTransactionManager));
            Guard.NotNull(ethereumClientBase, nameof(ethereumClientBase));

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.loggerFactory = loggerFactory;
            this.network = network;
            this.interopTransactionManager = interopTransactionManager;
            this.ethereumClientBase = ethereumClientBase;
        }

        [NoTrace]
        public override object Clone()
        {
            return new InteropBehavior(this.loggerFactory, this.network, this.interopTransactionManager, this.ethereumClientBase);
        }

        protected override void AttachCore()
        {
            this.logger.LogDebug("Attaching behaviour for {0}", this.AttachedPeer.PeerEndPoint.Address);
            this.AttachedPeer.MessageReceived.Register(this.OnMessageReceivedAsync, true);
        }

        protected override void DetachCore()
        {
            this.logger.LogDebug("Detaching behaviour for {0}", this.AttachedPeer.PeerEndPoint.Address);
            this.AttachedPeer.MessageReceived.Unregister(this.OnMessageReceivedAsync);
        }

        /// <summary>
        /// Broadcast the interop coordination request to federation members.
        /// </summary>
        /// <param name="payload">The payload to broadcast.</param>
        private async Task BroadcastAsync(InteropCoordinationPayload payload)
        {
            this.logger.LogDebug("Broadcasting to {0}", this.AttachedPeer.PeerEndPoint.Address);
            // TODO: Add federation check
            await this.AttachedPeer.SendMessageAsync(payload).ConfigureAwait(false);
        }

        private async Task OnMessageReceivedAsync(INetworkPeer peer, IncomingMessage message)
        {
            if (!(message.Message.Payload is InteropCoordinationPayload payload))
                return;

            this.logger.LogDebug("{0} received from '{1}':'{2}'.", nameof(InteropCoordinationPayload), peer.PeerEndPoint.Address, peer.RemoteSocketEndpoint.Address);

            // Check that the transaction ID in the payload actually exists, and is unconfirmed.
            BigInteger confirmationCount = this.ethereumClientBase.GetConfirmationCount(payload.TransactionId);

            // We presume that the initial submitter of the transaction must have at least confirmed it. Otherwise just ignore this coordination attempt.
            if (confirmationCount < 1)
            {
                this.logger.LogDebug("Multisig wallet transaction {0} has no confirmations.", payload.TransactionId);
                
                return;
            }

            this.logger.LogDebug("Multisig wallet transaction {0} has {1} confirmations.", payload.TransactionId, confirmationCount);

            // TODO: Add a signature to the payload that gets verified here
            this.interopTransactionManager.AddVote(payload.RequestId, payload.TransactionId);
        }
    }
}
