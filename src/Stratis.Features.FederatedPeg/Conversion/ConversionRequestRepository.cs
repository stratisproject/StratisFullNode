using System;
using System.Collections.Generic;

namespace Stratis.Features.FederatedPeg.Conversion
{
    /// <summary>Provides saving and retrieving functionality for objects of <see cref="ConversionRequest"/> type.</summary>
    public interface IConversionRequestRepository
    {
        /// <summary>Saves <see cref="ConversionRequest"/> to the repository.</summary>
        void Save(ConversionRequest request);

        /// <summary>Retrieves <see cref="ConversionRequest"/> with specified id.</summary>
        ConversionRequest Get(string requestId);

        /// <summary>Retrieves all mint requests.</summary>
        List<ConversionRequest> GetAllMint(bool onlyUnprocessed);

        /// <summary>Retrieves all burn requests.</summary>
        List<ConversionRequest> GetAllBurn(bool onlyUnprocessed);

        /// <summary>
        /// Deletes a particular conversion request.
        /// </summary>
        void DeleteConversionRequest(string requestId);

        /// <summary>
        /// Deletes all current unprocessed conversion requests.
        /// </summary>
        /// <returns>The amount unprocessed conversion requests that has been deleted.</returns>
        int DeleteConversionRequests();

        /// <summary>
        /// Set this node as the originator for a given conversion request.
        /// </summary>
        /// <param name="requestId">The request Id to set the state for.</param>
        /// <param name="requestStatus">The status to set the request to.</param>
        void SetConversionRequestState(string requestId, ConversionRequestStatus requestStatus);

        /// <summary>
        /// Sets a burn requests state.
        /// </summary>
        /// <param name="requestId">The request Id to set the state for.</param>
        /// <param name="blockHeight">The block height at which to reprocess the burn request.</param>
        /// <param name="requestStatus">The status to set the burn request to.</param>
        void ReprocessBurnRequest(string requestId, int blockHeight, ConversionRequestStatus requestStatus);
    }

    public class ConversionRequestRepository : IConversionRequestRepository
    {
        private IConversionRequestKeyValueStore KeyValueStore { get; }

        public ConversionRequestRepository(IConversionRequestKeyValueStore conversionRequestKeyValueStore)
        {
            this.KeyValueStore = conversionRequestKeyValueStore;
        }

        /// <inheritdoc />
        public void Save(ConversionRequest request)
        {
            this.KeyValueStore.SaveValue(request.RequestId, request);
        }

        /// <inheritdoc />
        public ConversionRequest Get(string requestId)
        {
            return this.KeyValueStore.LoadValue<ConversionRequest>(requestId);
        }

        /// <inheritdoc />
        public List<ConversionRequest> GetAllMint(bool onlyUnprocessed)
        {
            return this.KeyValueStore.GetAll(ConversionRequestType.Mint, onlyUnprocessed);
        }

        /// <inheritdoc />
        public List<ConversionRequest> GetAllBurn(bool onlyUnprocessed)
        {
            return this.KeyValueStore.GetAll(ConversionRequestType.Burn, onlyUnprocessed);
        }

        /// <inheritdoc />
        public int DeleteConversionRequests()
        {
            List<ConversionRequest> result = this.KeyValueStore.GetAll();

            foreach (ConversionRequest request in result)
            {
                this.KeyValueStore.Delete(request.RequestId);
            }

            return result.Count;
        }

        /// <inheritdoc />
        public void DeleteConversionRequest(string requestId)
        {
            this.KeyValueStore.Delete(requestId);
        }

        /// <inheritdoc />
        public void ReprocessBurnRequest(string requestId, int blockHeight, ConversionRequestStatus requestStatus)
        {
            ConversionRequest request = this.KeyValueStore.LoadValue<ConversionRequest>(requestId);
            if (request == null)
                throw new Exception($"{requestId} does not exist.");

            if (request.RequestType == ConversionRequestType.Mint)
                throw new Exception($"{requestId} is not a burn request.");

            if (blockHeight == 0)
                throw new Exception($"Block height cannot be 0.");

            request.BlockHeight = blockHeight;
            request.Processed = false;
            request.RequestStatus = requestStatus;

            this.KeyValueStore.SaveValue(request.RequestId, request, true);
        }

        /// <inheritdoc />
        public void SetConversionRequestState(string requestId, ConversionRequestStatus requestStatus)
        {
            ConversionRequest request = this.KeyValueStore.LoadValue<ConversionRequest>(requestId);
            if (request == null)
                throw new Exception($"{requestId} does not exist.");

            if (request.RequestStatus == ConversionRequestStatus.OriginatorSubmitting ||
                request.RequestStatus == ConversionRequestStatus.Processed)

                throw new Exception($"Request with a status of '{request.RequestStatus}' can not be set to { requestStatus}.");

            request.Processed = false;
            request.RequestStatus = requestStatus;

            this.KeyValueStore.SaveValue(request.RequestId, request, true);
        }
    }
}
