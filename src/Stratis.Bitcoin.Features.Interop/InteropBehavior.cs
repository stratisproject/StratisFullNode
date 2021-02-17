using System.Numerics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Features.Interop.EthereumClient;
using Stratis.Bitcoin.Features.Interop.Payloads;
using Stratis.Bitcoin.Features.PoA;
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

        private readonly IFederationManager federationManager;

        private readonly IInteropTransactionManager interopTransactionManager;

        private readonly IEthereumClientBase ethereumClientBase;

        public InteropBehavior(
            ILoggerFactory loggerFactory,
            Network network,
            IFederationManager federationManager,
            IInteropTransactionManager interopTransactionManager,
            IEthereumClientBase ethereumClientBase)
        {
            Guard.NotNull(loggerFactory, nameof(loggerFactory));
            Guard.NotNull(network, nameof(network));
            Guard.NotNull(federationManager, nameof(federationManager));
            Guard.NotNull(interopTransactionManager, nameof(interopTransactionManager));
            Guard.NotNull(ethereumClientBase, nameof(ethereumClientBase));

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.loggerFactory = loggerFactory;
            this.network = network;
            this.federationManager = federationManager;
            this.interopTransactionManager = interopTransactionManager;
            this.ethereumClientBase = ethereumClientBase;
        }

        [NoTrace]
        public override object Clone()
        {
            return new InteropBehavior(this.loggerFactory, this.network, this.federationManager, this.interopTransactionManager, this.ethereumClientBase);
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

        private async Task OnMessageReceivedAsync(INetworkPeer peer, IncomingMessage message)
        {
            if (!(message.Message.Payload is InteropCoordinationPayload payload))
                return;

            this.logger.LogInformation("{0} received from '{1}':'{2}'. Request {3} proposing transaction ID {4}.", nameof(InteropCoordinationPayload), peer.PeerEndPoint.Address, peer.RemoteSocketEndpoint.Address, payload.RequestId, payload.TransactionId);

            // Check that the payload is signed by a federation member.
            PubKey pubKey = PubKey.RecoverFromMessage(payload.RequestId + payload.TransactionId, payload.Signature);

            if (pubKey == null || !this.federationManager.IsMultisigMember(pubKey))
            {
                this.logger.LogWarning("Received unverified coordination payload for {0}. Computed pubkey {1}.", payload.RequestId, pubKey?.ToHex());
            }

            // Check that the transaction ID in the payload actually exists, and is unconfirmed.
            BigInteger confirmationCount = this.ethereumClientBase.GetConfirmationCount(payload.TransactionId);

            // We presume that the initial submitter of the transaction must have at least confirmed it. Otherwise just ignore this coordination attempt.
            if (confirmationCount < 1)
            {
                this.logger.LogInformation("Multisig wallet transaction {0} has no confirmations.", payload.TransactionId);
                
                return;
            }

            this.logger.LogInformation("Multisig wallet transaction {0} has {1} confirmations (request ID: {2}).", payload.TransactionId, confirmationCount, payload.RequestId);

            this.interopTransactionManager.AddVote(payload.RequestId, payload.TransactionId, pubKey);
        }
    }
}
