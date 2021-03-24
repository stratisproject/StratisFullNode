using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using NLog;

namespace Stratis.Bitcoin.Features.FederatedPeg
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

        public void Save(ConversionRequest request)
        {
            this.logger.Debug($"Saving conversion request {request.RequestId} to store.");

            this.KeyValueStore.SaveValue(request.RequestId, request);
        }

        public ConversionRequest Get(string requestId)
        {
            this.logger.Debug($"Retrieving conversion request {requestId} from store.");

            return this.KeyValueStore.LoadValue<ConversionRequest>(requestId);
        }

        public List<ConversionRequest> GetAllMint(bool onlyUnprocessed)
        {
            this.logger.Debug($"Retrieving all mint requests from store, {nameof(onlyUnprocessed)}={onlyUnprocessed}");

            return this.KeyValueStore.GetAll((int)ConversionRequestType.Mint, onlyUnprocessed);
        }

        public List<ConversionRequest> GetAllBurn(bool onlyUnprocessed)
        {
            this.logger.Debug($"Retrieving all burn requests from store, {nameof(onlyUnprocessed)}={onlyUnprocessed}");

            return this.KeyValueStore.GetAll((int)ConversionRequestType.Burn, onlyUnprocessed);
        }
    }
}
