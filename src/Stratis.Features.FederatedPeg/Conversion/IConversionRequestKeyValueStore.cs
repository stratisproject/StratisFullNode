using System.Collections.Generic;
using Stratis.Bitcoin.Persistence;

namespace Stratis.Bitcoin.Features.FederatedPeg
{
    public interface IConversionRequestKeyValueStore : IKeyValueRepository
    {
        List<ConversionRequest> GetAll(int type, bool onlyUnprocessed);
    }
}
