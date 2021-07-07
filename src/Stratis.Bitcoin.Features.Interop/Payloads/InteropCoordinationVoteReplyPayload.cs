﻿using NBitcoin;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.P2P.Protocol.Payloads;

namespace Stratis.Bitcoin.Features.Interop.Payloads
{
    [Payload("coordreply")]
    public sealed class InteropCoordinationVoteReplyPayload : Payload
    {
        private string requestId;
        private int transactionId;
        private string signature;
        private int destinationChain;

        public string RequestId => this.requestId;

        public int TransactionId => this.transactionId;

        public string Signature => this.signature;

        public DestinationChain DestinationChain { get { return (DestinationChain)this.destinationChain; } set { this.destinationChain = (int)value; } }

        /// <summary>Parameterless constructor needed for deserialization.</summary>
        public InteropCoordinationVoteReplyPayload()
        {
        }

        public InteropCoordinationVoteReplyPayload(string requestId, int transactionId, string signature, DestinationChain destinationChain)
        {
            this.requestId = requestId;
            this.transactionId = transactionId;
            this.signature = signature;
            this.destinationChain = (int)destinationChain;
        }

        /// <inheritdoc/>
        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.requestId);
            stream.ReadWrite(ref this.transactionId);
            stream.ReadWrite(ref this.signature);
            stream.ReadWrite(ref this.destinationChain);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{nameof(this.Command)}:'{this.Command}',{nameof(this.RequestId)}:'{this.RequestId}',{nameof(this.TransactionId)}:'{this.TransactionId}'";
        }
    }
}
