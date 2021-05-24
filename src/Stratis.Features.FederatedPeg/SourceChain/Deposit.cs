using NBitcoin;
using Newtonsoft.Json;
using Stratis.Features.FederatedPeg.Conversion;
using Stratis.Features.FederatedPeg.Interfaces;

namespace Stratis.Features.FederatedPeg.SourceChain
{
    public class Deposit : IDeposit
    {
        public Deposit(uint256 id, DepositRetrievalType retrievalType, Money amount, string targetAddress, DestinationChain targetChain, int blockNumber, uint256 blockHash)
        {
            this.Id = id;
            this.RetrievalType = retrievalType;
            this.Amount = amount;
            this.TargetAddress = targetAddress;
            this.TargetChain = targetChain;
            this.BlockNumber = blockNumber;
            this.BlockHash = blockHash;
        }

        /// <inheritdoc />
        public uint256 Id { get; }

        /// <inheritdoc />
        public Money Amount { get; }

        /// <inheritdoc />
        public string TargetAddress { get; }

        /// <inheritdoc />
        public DestinationChain TargetChain { get; }

        /// <inheritdoc />
        public int BlockNumber { get; }

        /// <inheritdoc />
        public uint256 BlockHash { get; }

        /// <inheritdoc />
        public DepositRetrievalType RetrievalType { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
    }
}