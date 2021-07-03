using NBitcoin;
using Stratis.Bitcoin.P2P.Protocol.Payloads;
using Stratis.Features.FederatedPeg.InputConsolidation;

namespace Stratis.Features.FederatedPeg.Payloads
{
    [Payload("partial")]
    public class RequestPartialTransactionPayload : Payload
    {
        public static readonly uint256 ConsolidationDepositId = uint256.Zero;

        private uint256 depositId;
        private Transaction transactionPartial;

        /// <summary>
        /// Deposit ID of the withdrawal transaction we are signing. We will use a DepositId of 0 as a special case to mean a consolidation transaction.
        /// See <see cref="InputConsolidator"/>
        /// </summary>
        public uint256 DepositId => this.depositId;
        public Transaction PartialTransaction => this.transactionPartial;

        /// <remarks>Needed for deserialization.</remarks>
        public RequestPartialTransactionPayload()
        {
        }

        public RequestPartialTransactionPayload(uint256 depositId)
        {
            this.depositId = depositId;
        }

        public RequestPartialTransactionPayload AddPartial(Transaction partialTransaction)
        {
            this.transactionPartial = partialTransaction;
            return this;
        }

        public override void ReadWriteCore(BitcoinStream stream)
        {
            stream.ReadWrite(ref this.depositId);
            stream.ReadWrite(ref this.transactionPartial);
        }

        public override string ToString()
        {
            return $"{nameof(this.Command)}:'{this.Command}',{nameof(this.DepositId)}:'{this.DepositId}'";
        }
    }
}
