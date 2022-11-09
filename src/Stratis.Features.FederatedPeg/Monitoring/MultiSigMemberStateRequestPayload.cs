using NBitcoin;
using Stratis.Bitcoin.P2P.Protocol.Payloads;

namespace Stratis.Features.FederatedPeg.Monitoring
{
    [Payload("msstatereqst")]
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

        public string MemberToCheck { get { return this.memberToCheck; } set { this.memberToCheck = value; } }

        public string Signature { get { return this.signature; } }

        public int CrossChainStoreHeight { get { return this.crossChainStoreHeight; } set { this.crossChainStoreHeight = value; } }
        public int CrossChainStoreNextDepositHeight { get { return this.crossChainStoreNextDepositHeight; } set { this.crossChainStoreNextDepositHeight = value; } }
        public int PartialTransactions { get { return this.partialTransactions; } set { this.partialTransactions = value; } }
        public int SuspendedPartialTransactions { get { return this.suspendedPartialTransactions; } set { this.suspendedPartialTransactions = value; } }

        /// <summary>Parameterless constructor needed for deserialization.</summary>
        public MultiSigMemberStateRequestPayload()
        {
        }

        private MultiSigMemberStateRequestPayload(string memberToCheck, bool isRequesting, string signature)
        {
            this.memberToCheck = memberToCheck;
            this.isRequesting = isRequesting;
            this.signature = signature;
        }

        /// <inheritdoc/>
        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.memberToCheck);
            stream.ReadWrite(ref this.isRequesting);
            stream.ReadWrite(ref this.signature);
            stream.ReadWriteNullIntField(ref this.crossChainStoreHeight);
            stream.ReadWriteNullIntField(ref this.crossChainStoreNextDepositHeight);
            stream.ReadWriteNullIntField(ref this.partialTransactions);
            stream.ReadWriteNullIntField(ref this.suspendedPartialTransactions);
        }

        public static MultiSigMemberStateRequestPayload Request(string memberToCheck, string signature)
        {
            return new MultiSigMemberStateRequestPayload(memberToCheck, true, signature);
        }

        public static MultiSigMemberStateRequestPayload Reply(string memberToCheck, string signature)
        {
            return new MultiSigMemberStateRequestPayload(memberToCheck, false, signature);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{nameof(this.Command)}:'{this.Command}',{nameof(this.MemberToCheck)}:'{this.MemberToCheck}'";
        }
    }
}