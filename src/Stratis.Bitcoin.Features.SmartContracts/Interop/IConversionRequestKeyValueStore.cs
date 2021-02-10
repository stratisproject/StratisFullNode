using System.Collections.Generic;
using Stratis.Bitcoin.Persistence;

namespace Stratis.Bitcoin.Features.SmartContracts.Interop
{
    public interface IConversionRequestKeyValueStore : IKeyValueRepository
    {
        List<ConversionRequest> GetAll(int type, bool onlyUnprocessed);
    }
}
