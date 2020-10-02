using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Features.FederatedPeg.Distribution
{
    public class DistributionStore : IDistributionStore
    {
        private readonly IKeyValueRepository keyValueRepository;

        private const string DistributionsKey = "rewardDistributions";

        private readonly List<DistributionRecord> distributionRecords;

        public DistributionStore(IKeyValueRepository keyValueRepository)
        {
            this.keyValueRepository = keyValueRepository;

            this.distributionRecords = this.keyValueRepository.LoadValueJson<List<DistributionRecord>>(DistributionsKey);
        }

        /// <inheritdoc />
        public void AddToStore(DistributionRecord record)
        {
            this.distributionRecords.Add(record);
        }

        /// <inheritdoc />
        public List<DistributionRecord> GetMatured(int height)
        {
            return this.distributionRecords.Where(r => r.CommitmentHeight <= height && !r.Processed).ToList();
        }

        /// <inheritdoc />
        public void FlagAsProcessed(uint256 txId)
        {
            DistributionRecord record = this.distributionRecords.FirstOrDefault(r => r.TransactionId == txId);

            if (record != null)
                record.Processed = true;
        }

        /// <inheritdoc />
        public void Save()
        {
            this.keyValueRepository.SaveValueJson(DistributionsKey, this.distributionRecords);
        }
    }
}
