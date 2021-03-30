using System.Collections.Generic;
using Stratis.Bitcoin.Persistence;

namespace Stratis.Features.FederatedPeg.Conversion
{
    public interface IConversionRequestKeyValueStore : IKeyValueRepository
    {
        List<ConversionRequest> GetAll(int type, bool onlyUnprocessed);
    }
}
