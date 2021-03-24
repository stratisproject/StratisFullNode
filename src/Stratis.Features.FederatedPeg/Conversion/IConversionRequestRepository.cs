using System.Collections.Generic;

namespace Stratis.Bitcoin.Features.FederatedPeg
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
    }
}
