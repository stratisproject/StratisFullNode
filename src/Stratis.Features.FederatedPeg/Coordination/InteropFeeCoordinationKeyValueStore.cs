using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Persistence;
using Stratis.Bitcoin.Persistence.KeyValueStores;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Features.FederatedPeg.Coordination
{
    /// <summary>
    /// This is implemented separately to <see cref="KeyValueRepository"/> so that the repository can live in its own folder on disk.
    /// </summary>
    public interface IInteropFeeCoordinationKeyValueStore : IKeyValueRepository
    {
    }

    public sealed class InteropFeeCoordinationKeyValueStore : LevelDbKeyValueRepository, IInteropFeeCoordinationKeyValueStore
    {
        public InteropFeeCoordinationKeyValueStore(DataFolder dataFolder, DBreezeSerializer dBreezeSerializer) : this(dataFolder.InteropFeeRepositoryPath, dBreezeSerializer)
        {
        }

        public InteropFeeCoordinationKeyValueStore(string folder, DBreezeSerializer dBreezeSerializer) : base(folder, dBreezeSerializer)
        {
        }
    }
}
