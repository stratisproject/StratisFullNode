using System.Collections.Generic;

namespace Stratis.Bitcoin.Features.SmartContracts.Interop
{
    public interface IConversionRequestRepository
    {
        void Save(ConversionRequest request);

        ConversionRequest Get(string requestId);

        List<ConversionRequest> GetAllMint(bool onlyUnprocessed);

        List<ConversionRequest> GetAllBurn(bool onlyUnprocessed);
    }
}
