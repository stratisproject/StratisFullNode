using System;
using System.Numerics;
using System.Threading.Tasks;
using NBitcoin;
using NLog;
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
        private readonly NLog.ILogger logger;

        private readonly Network network;

        private readonly IFederationManager federationManager;

        private readonly IInteropTransactionManager interopTransactionManager;

        private readonly IEthereumClientBase ethereumClientBase;

        public InteropBehavior(
            Network network,
            IFederationManager federationManager,
            IInteropTransactionManager interopTransactionManager,
            IEthereumClientBase ethereumClientBase)
        {
            Guard.NotNull(network, nameof(network));
            Guard.NotNull(federationManager, nameof(federationManager));
            Guard.NotNull(interopTransactionManager, nameof(interopTransactionManager));
            Guard.NotNull(ethereumClientBase, nameof(ethereumClientBase));

            this.logger = LogManager.GetCurrentClassLogger();
            this.network = network;
            this.federationManager = federationManager;
            this.interopTransactionManager = interopTransactionManager;
            this.ethereumClientBase = ethereumClientBase;
        }

        [NoTrace]
        public override object Clone()
        {
            return new InteropBehavior(this.network, this.federationManager, this.interopTransactionManager, this.ethereumClientBase);
        }

        protected override void AttachCore()
        {
            this.logger.Debug("Attaching behaviour for {0}", this.AttachedPeer.PeerEndPoint.Address);
            this.AttachedPeer.MessageReceived.Register(this.OnMessageReceivedAsync, true);
        }

        protected override void DetachCore()
        {
            this.logger.Debug("Detaching behaviour for {0}", this.AttachedPeer.PeerEndPoint.Address);
            this.AttachedPeer.MessageReceived.Unregister(this.OnMessageReceivedAsync);
        }

        private async Task OnMessageReceivedAsync(INetworkPeer peer, IncomingMessage message)
        {
            if (!(message.Message.Payload is InteropCoordinationPayload payload))
                return;

            if (!this.federationManager.IsFederationMember)
                return;

            this.logger.Info("{0} received from '{1}':'{2}'. Request {3} proposing transaction ID {4}.", nameof(InteropCoordinationPayload), peer.PeerEndPoint.Address, peer.RemoteSocketEndpoint.Address, payload.RequestId, payload.TransactionId);

            if (payload.TransactionId == BigInteger.MinusOne)
                return;

            // Check that the payload is signed by a federation member.
            PubKey pubKey;

            try
            {
                pubKey = PubKey.RecoverFromMessage(payload.RequestId + payload.TransactionId, payload.Signature);

                if (!this.federationManager.IsMultisigMember(pubKey))
                {
                    this.logger.Warn("Received unverified coordination payload for {0}. Computed pubkey {1}.", payload.RequestId, pubKey?.ToHex());

                    return;
                }
            }
            catch (Exception)
            {
                this.logger.Warn("Received malformed coordination payload for {0}.", payload.RequestId);

                return;
            }

            BigInteger confirmationCount;

            try
            {
                // Check that the transaction ID in the payload actually exists, and is unconfirmed.
                confirmationCount = await this.ethereumClientBase.GetConfirmationCountAsync(payload.TransactionId).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }

            // We presume that the initial submitter of the transaction must have at least confirmed it. Otherwise just ignore this coordination attempt.
            if (confirmationCount < 1)
            {
                this.logger.Info("Multisig wallet transaction {0} has no confirmations.", payload.TransactionId);
                
                return;
            }

            this.logger.Info("Multisig wallet transaction {0} has {1} confirmations (request ID: {2}).", payload.TransactionId, confirmationCount, payload.RequestId);

            this.interopTransactionManager.AddVote(payload.RequestId, payload.TransactionId, pubKey);
        }
    }
}
