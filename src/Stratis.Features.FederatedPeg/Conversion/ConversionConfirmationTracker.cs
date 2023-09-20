using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Persistence;

namespace Stratis.Features.FederatedPeg.Conversion
{
    public interface IConversionConfirmationTracker
    {
        List<PubKey> GetParticipants(string requestId);

        void RecordParticipant(string requestId, PubKey participant);
    }

    public class ConversionConfirmationTracker : IConversionConfirmationTracker
    {
        public const string ConversionConfirmationTrackerKey = "ConversionConfirmationTrackerKey";

        private readonly IKeyValueRepository keyValueRepository;

        private readonly Dictionary<string, HashSet<PubKey>> confirmations;

        private object lockObject = new object();

        public ConversionConfirmationTracker(IKeyValueRepository keyValueRepository)
        {
            this.keyValueRepository = keyValueRepository;

            lock (this.lockObject)
            {
                this.confirmations = this.keyValueRepository.LoadValueJson<Dictionary<string, HashSet<PubKey>>>(ConversionConfirmationTrackerKey);

                if (this.confirmations == null)
                {
                    this.confirmations = new Dictionary<string, HashSet<PubKey>>();
                    this.keyValueRepository.SaveValueJson(ConversionConfirmationTrackerKey, this.confirmations, true);
                }
            }
        }

        public void RecordParticipant(string requestId, PubKey participant)
        {
            lock (this.lockObject)
            {
                HashSet<PubKey> currentConfirmations = this.confirmations[requestId];

                if (currentConfirmations == null)
                {
                    currentConfirmations = new HashSet<PubKey>();
                }

                currentConfirmations.Add(participant);
                this.confirmations[requestId] = currentConfirmations;
                this.keyValueRepository.SaveValueJson(ConversionConfirmationTrackerKey, this.confirmations, true);
            }
        }

        public List<PubKey> GetParticipants(string requestId)
        {
            lock (this.lockObject)
            {
                return this.confirmations[requestId].ToList();
            }
        }
    }
}
