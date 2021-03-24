using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using NLog;

namespace Stratis.Bitcoin.Features.Interop
{
    public interface IInteropRequestRepository
    {
        void Save(InteropRequest request);

        InteropRequest Get(string requestId);

        List<InteropRequest> GetAllEthereum(bool onlyUnprocessed);

        List<InteropRequest> GetAllStratis(bool onlyUnprocessed);
    }

    public class InteropRequestRepository : IInteropRequestRepository
    {
        private const string TableName = "InteropRequests";

        public IInteropRequestKeyValueStore KeyValueStore { get; }

        private readonly NLog.ILogger logger;

        public InteropRequestRepository(IInteropRequestKeyValueStore interopRequestKeyValueStore)
        {
            this.KeyValueStore = interopRequestKeyValueStore;

            this.logger = LogManager.GetCurrentClassLogger();
        }

        public void Save(InteropRequest request)
        {
            this.logger.Debug($"Saving interop request {request.RequestId} to store.");

            this.KeyValueStore.SaveValue(request.RequestId, request);
        }

        public InteropRequest Get(string requestId)
        {
            this.logger.Debug($"Retrieving interop request {requestId} from store.");

            return this.KeyValueStore.LoadValue<InteropRequest>(requestId);
        }

        public List<InteropRequest> GetAllEthereum(bool onlyUnprocessed)
        {
            this.logger.Debug($"Retrieving all Ethereum interop requests from store, {nameof(onlyUnprocessed)}={onlyUnprocessed}");
            
            return this.KeyValueStore.GetAll((int)InteropRequestType.InvokeEthereum, onlyUnprocessed);
        }

        public List<InteropRequest> GetAllStratis(bool onlyUnprocessed)
        {
            this.logger.Debug($"Retrieving all Stratis interop requests from store, {nameof(onlyUnprocessed)}={onlyUnprocessed}");

            return this.KeyValueStore.GetAll((int)InteropRequestType.InvokeStratis, onlyUnprocessed);
        }
    }
}
