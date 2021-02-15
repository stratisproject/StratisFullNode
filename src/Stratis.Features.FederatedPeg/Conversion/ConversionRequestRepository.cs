using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Stratis.Bitcoin.Features.FederatedPeg
{
    public class ConversionRequestRepository : IConversionRequestRepository
    {
        private IConversionRequestKeyValueStore KeyValueStore { get; }

        private readonly ILogger logger;

        public ConversionRequestRepository(ILoggerFactory loggerFactory, IConversionRequestKeyValueStore conversionRequestKeyValueStore)
        {
            this.KeyValueStore = conversionRequestKeyValueStore;

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        public void Save(ConversionRequest request)
        {
            this.logger.LogDebug($"Saving conversion request {request.RequestId} to store.");

            this.KeyValueStore.SaveValue(request.RequestId, request);
        }

        public ConversionRequest Get(string requestId)
        {
            this.logger.LogDebug($"Retrieving conversion request {requestId} from store.");

            return this.KeyValueStore.LoadValue<ConversionRequest>(requestId);
        }

        public List<ConversionRequest> GetAllMint(bool onlyUnprocessed)
        {
            this.logger.LogDebug($"Retrieving all mint requests from store, {nameof(onlyUnprocessed)}={onlyUnprocessed}");

            return this.KeyValueStore.GetAll((int)ConversionRequestType.Mint, onlyUnprocessed);
        }

        public List<ConversionRequest> GetAllBurn(bool onlyUnprocessed)
        {
            this.logger.LogDebug($"Retrieving all burn requests from store, {nameof(onlyUnprocessed)}={onlyUnprocessed}");

            return this.KeyValueStore.GetAll((int)ConversionRequestType.Burn, onlyUnprocessed);
        }
    }
}
