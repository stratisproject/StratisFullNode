using System.Collections.Generic;
using NBitcoin;

namespace Stratis.Features.FederatedPeg.TargetChain
{
    public sealed class RecordLatestMatureDepositsResult
    {
        public RecordLatestMatureDepositsResult()
        {
            this.WithdrawalTransactions = new List<Transaction>();
        }

        public bool MatureDepositRecorded { get; private set; }

        public List<Transaction> WithdrawalTransactions { get; }

        public RecordLatestMatureDepositsResult Succeeded()
        {
            this.MatureDepositRecorded = true;
            return this;
        }
    }
}
