using NBitcoin;
using Newtonsoft.Json;
using Stratis.Bitcoin.Utilities.JsonConverters;
using Stratis.Features.FederatedPeg.Conversion;
using Stratis.Features.FederatedPeg.SourceChain;

namespace Stratis.Features.FederatedPeg.Interfaces
{
    /// <summary>
    /// Represents a deposit made to a sidechain mutlisig, with the aim of triggering
    /// a cross chain transfer.
    /// </summary>
    public interface IDeposit
    {
        /// <summary>The Id (or hash) of the source transaction that originates the fund transfer.</summary>
        [JsonConverter(typeof(UInt256JsonConverter))]
        uint256 Id { get; }

        /// <summary>Whether the deposit is a "faster" or "normal" deposit.</summary>
        DepositRetrievalType RetrievalType { get; }

        /// <summary>The amount of the requested fund transfer.</summary>
        [JsonConverter(typeof(MoneyJsonConverter))]
        Money Amount { get; }

        /// <summary>The target address, on the target chain, for the fund deposited on the multisig.</summary>
        string TargetAddress { get; }

        /// <summary>Chain on which STRAX minting or burning should occur.</summary>
        public DestinationChain TargetChain { get; }

        /// <summary>The block number where the source deposit has been persisted.</summary>
        int BlockNumber { get; }

        /// <summary>The hash of the block where the source deposit has been persisted.</summary>
        [JsonConverter(typeof(UInt256JsonConverter))]
        uint256 BlockHash { get; }
    }
}