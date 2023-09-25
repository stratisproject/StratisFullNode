using System;
using System.Collections.Generic;
using NBitcoin;

namespace Stratis.Features.FederatedPeg.Conversion
{
    /// <summary>Provides saving and retrieving functionality for objects of <see cref="ConversionRequestCoordinationVote"/> type.</summary>
    public interface IConversionRequestCoordinationVoteRepository
    {
        /// <summary>Saves <see cref="ConversionRequestCoordinationVote"/> to the repository.</summary>
        void Save(ConversionRequestCoordinationVote vote);

        /// <summary>Retrieves <see cref="ConversionRequestCoordinationVote"/> with specified id and pubkey.</summary>
        ConversionRequestCoordinationVote Get(string requestId, PubKey pubKey);

        /// <summary>Retrieves list of all <see cref="ConversionRequestCoordinationVote"/> currently saved.</summary>
        List<ConversionRequestCoordinationVote> GetAll();

        /// <summary>Retrieves list of <see cref="ConversionRequestCoordinationVote"/> for specified request id.</summary>
        List<ConversionRequestCoordinationVote> GetAll(string requestId);

        /// <summary>
        /// Deletes a particular conversion request coordination vote.
        /// </summary>
        void Delete(string requestId, PubKey pubKey);

        /// <summary>
        /// Deletes all current unprocessed conversion request coordination votes.
        /// </summary>
        /// <returns>The amount of conversion request coordination votes that have been deleted.</returns>
        int DeleteAll();
    }

    public class ConversionRequestCoordinationVoteRepository : IConversionRequestCoordinationVoteRepository
    {
        private IConversionRequestCoordinationVoteKeyValueStore KeyValueStore { get; }

        private string MakeKey(string requestId, PubKey pubKey)
        {
            return $"{requestId}:{pubKey.ToHex()}";
        }

        public ConversionRequestCoordinationVoteRepository(IConversionRequestCoordinationVoteKeyValueStore conversionRequestKeyValueStore)
        {
            this.KeyValueStore = conversionRequestKeyValueStore;
        }

        /// <inheritdoc />
        public void Save(ConversionRequestCoordinationVote vote)
        {
            this.KeyValueStore.SaveValue(MakeKey(vote.RequestId, vote.PubKey), vote);
        }

        /// <inheritdoc />
        public ConversionRequestCoordinationVote Get(string requestId, PubKey pubKey)
        {
            return this.KeyValueStore.LoadValue<ConversionRequestCoordinationVote>(MakeKey(requestId, pubKey));
        }

        /// <inheritdoc />
        public List<ConversionRequestCoordinationVote> GetAll()
        {
            return this.KeyValueStore.GetAll();
        }

        /// <inheritdoc />
        public List<ConversionRequestCoordinationVote> GetAll(string requestId)
        {
            return this.KeyValueStore.GetAll(requestId);
        }

        /// <inheritdoc />
        public int DeleteAll()
        {
            List<ConversionRequestCoordinationVote> result = this.KeyValueStore.GetAll();

            foreach (ConversionRequestCoordinationVote vote in result)
            {
                this.KeyValueStore.Delete(MakeKey(vote.RequestId, vote.PubKey));
            }

            return result.Count;
        }

        /// <inheritdoc />
        public void Delete(string requestId, PubKey pubKey)
        {
            this.KeyValueStore.Delete(MakeKey(requestId, pubKey));
        }
    }
}
