using System.Collections.Generic;
using NBitcoin;

namespace Stratis.Features.FederatedPeg.Distribution
{
    public interface IDistributionStore
    {
        /// <summary>
        /// Adds a distribution record to the store.
        /// </summary>
        void AddToStore(DistributionRecord record);

        /// <summary>
        /// Retrieves a list of all the unprocessed distribution records that are below a given block height.
        /// </summary>
        List<DistributionRecord> GetMatured(int height);

        /// <summary>
        /// Marks a distribution record as processed so that it will be ignored going forwards.
        /// </summary>
        void FlagAsProcessed(uint256 txId);

        /// <summary>
        /// Persist the current store contents to disk.
        /// </summary>
        void Save();
    }
}
