using System.Linq;
using System.Numerics;
using Newtonsoft.Json;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Persistence;
using Stratis.Bitcoin.Persistence.KeyValueStores;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Features.FederatedPeg.Conversion
{
    /// <summary>
    /// This is implemented separately to <see cref="KeyValueRepository"/> so that the repository can live in its own folder on disk.
    /// </summary>
    public interface IReplenishmentKeyValueStore : IKeyValueRepository
    {
        ReplenishmentTransaction FindUnprocessed();
    }

    /// <summary>
    /// Stores wSTRAX replenishment transactions on disk.
    /// </summary>
    public sealed class ReplenishmentKeyValueStore : LevelDbKeyValueRepository, IReplenishmentKeyValueStore
    {
        public ReplenishmentKeyValueStore(DataFolder dataFolder, DBreezeSerializer dBreezeSerializer) : this(dataFolder.InteropFeeRepositoryPath, dBreezeSerializer)
        {
        }

        public ReplenishmentKeyValueStore(string folder, DBreezeSerializer dBreezeSerializer) : base(folder, dBreezeSerializer)
        {
        }

        public ReplenishmentTransaction FindUnprocessed()
        {
            ReplenishmentTransaction replenishment = this.GetAllAsJson<ReplenishmentTransaction>().FirstOrDefault(r => !r.Processed);
            return replenishment;
        }
    }

    public sealed class ReplenishmentTransaction
    {
        [JsonProperty(PropertyName = "transactionHash")]
        public string TransactionHash { get; set; }

        [JsonProperty(PropertyName = "transactionId")]
        public BigInteger TransactionId { get; set; }

        [JsonProperty(PropertyName = "processed")]
        public bool Processed { get; set; }
    }
}
