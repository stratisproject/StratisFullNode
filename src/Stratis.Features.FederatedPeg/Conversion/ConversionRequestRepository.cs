using System.Collections.Generic;
using NLog;

namespace Stratis.Features.FederatedPeg.Conversion
{
    public class ConversionRequestRepository : IConversionRequestRepository
    {
        private IConversionRequestKeyValueStore KeyValueStore { get; }

        private readonly NLog.ILogger logger;

        public ConversionRequestRepository(IConversionRequestKeyValueStore conversionRequestKeyValueStore)
        {
            this.KeyValueStore = conversionRequestKeyValueStore;

            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <inheritdoc />
        public void Save(ConversionRequest request)
        {
            this.logger.Debug($"Saving conversion request {request.RequestId} to store.");

            this.KeyValueStore.SaveValue(request.RequestId, request);
        }

        /// <inheritdoc />
        public ConversionRequest Get(string requestId)
        {
            this.logger.Debug($"Retrieving conversion request {requestId} from store.");

            return this.KeyValueStore.LoadValue<ConversionRequest>(requestId);
        }

        /// <inheritdoc />
        public List<ConversionRequest> GetAllMint(bool onlyUnprocessed)
        {
            this.logger.Debug($"Retrieving all mint requests from store, {nameof(onlyUnprocessed)}={onlyUnprocessed}");

            return this.KeyValueStore.GetAll((int)ConversionRequestType.Mint, onlyUnprocessed);
        }

        /// <inheritdoc />
        public List<ConversionRequest> GetAllBurn(bool onlyUnprocessed)
        {
            this.logger.Debug($"Retrieving all burn requests from store, {nameof(onlyUnprocessed)}={onlyUnprocessed}");

            return this.KeyValueStore.GetAll((int)ConversionRequestType.Burn, onlyUnprocessed);
        }
    }
}
