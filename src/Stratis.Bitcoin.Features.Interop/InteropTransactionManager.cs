using System.Collections.Generic;
using System.Numerics;

namespace Stratis.Bitcoin.Features.Interop
{
    public class InteropTransactionManager : IInteropTransactionManager
    {
        private Dictionary<string, Dictionary<BigInteger, int>> activeVotes;

        private readonly object lockObject = new object();

        public void AddVote(string requestId, BigInteger transactionId)
        {
            lock (this.lockObject)
            {
                if (!this.activeVotes.TryGetValue(requestId, out Dictionary<BigInteger, int> transactionIdVotes))
                    transactionIdVotes = new Dictionary<BigInteger, int>();

                if (!transactionIdVotes.ContainsKey(transactionId))
                    transactionIdVotes[transactionId] = 1;
                else
                    transactionIdVotes[transactionId]++;

                this.activeVotes[requestId] = transactionIdVotes;
            }
        }

        public BigInteger GetAgreedTransactionId(string requestId, int quorum)
        {
            lock (this.lockObject)
            {
                if (!this.activeVotes.ContainsKey(requestId))
                    return BigInteger.MinusOne;

                BigInteger highestVoted = BigInteger.MinusOne;
                int voteCount = 0;
                foreach (KeyValuePair<BigInteger, int> vote in this.activeVotes[requestId])
                {
                    if (vote.Value > voteCount && vote.Value >= quorum)
                    {
                        highestVoted = vote.Key;
                        voteCount = vote.Value;
                    }
                }

                return highestVoted;
            }
        }
        
        public void RemoveTransaction(string requestId)
        {
            lock (this.lockObject)
            {
                this.activeVotes.Remove(requestId);
            }
        }
    }
}
