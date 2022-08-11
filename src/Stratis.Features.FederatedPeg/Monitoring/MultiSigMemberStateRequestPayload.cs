using NBitcoin;
using Stratis.Bitcoin.P2P.Protocol.Payloads;

namespace Stratis.Features.FederatedPeg.Monitoring
{
    [Payload("convrqst")]
    public sealed class MultiSigMemberStateRequestPayload : Payload
    {
        private int crossChainStoreHeight;
        private int crossChainStoreNextDepositHeight;
        private int partialTransactions;
        private int suspendedPartialTransactions;

        private bool isRequesting;
        private string memberToCheck;
        private string signature;

        /// <summary>
        /// <c>True</c> if this payload is requesting state from another multisig member.
        /// <c>False</c> if it is replying.
        /// </summary>
        public bool IsRequesting { get { return this.isRequesting; } }

        public string MemberToCheck { get { return this.memberToCheck; } }

        public string Signature { get { return this.signature; } }

        public int CrossChainStoreHeight { get { return this.crossChainStoreHeight; } set { this.crossChainStoreHeight = value; } }
        public int CrossChainStoreNextDepositHeight { get { return this.crossChainStoreNextDepositHeight; } set { this.crossChainStoreNextDepositHeight = value; } }
        public int PartialTransactions { get { return this.partialTransactions; } set { this.partialTransactions = value; } }
        public int SuspendedPartialTransactions { get { return this.suspendedPartialTransactions; } set { this.suspendedPartialTransactions = value; } }

        /// <summary>Parameterless constructor needed for deserialization.</summary>
        public MultiSigMemberStateRequestPayload()
        {
        }

        private MultiSigMemberStateRequestPayload(string signature, bool isRequesting)
        {
            this.isRequesting = isRequesting;
            this.signature = signature;
        }

        private MultiSigMemberStateRequestPayload(string memberToCheck, string signature, bool isRequesting) : this(signature, isRequesting)
        {
            this.memberToCheck = memberToCheck;
        }

        /// <inheritdoc/>
        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.isRequesting);
            stream.ReadWrite(ref this.memberToCheck);
            stream.ReadWrite(ref this.signature);
            stream.ReadWriteNullIntField(ref this.crossChainStoreHeight);
            stream.ReadWriteNullIntField(ref this.crossChainStoreNextDepositHeight);
            stream.ReadWriteNullIntField(ref this.partialTransactions);
            stream.ReadWriteNullIntField(ref this.suspendedPartialTransactions);
        }

        public static MultiSigMemberStateRequestPayload Request(string memberToCheck, string signature)
        {
            return new MultiSigMemberStateRequestPayload(memberToCheck, signature, true);
        }

        public static MultiSigMemberStateRequestPayload Reply(string signature)
        {
            return new MultiSigMemberStateRequestPayload(signature, false);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{nameof(this.Command)}:'{this.Command}'";
        }
    }
}