using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Consensus;

namespace Stratis.Bitcoin
{
    public static class NodeHelpers
    {
        public static DbType GetDbType(this NodeSettings nodeSettings)
        {
            var dbTypeString = nodeSettings.ConfigReader.GetOrDefault("dbtype", "leveldb");

            DbType dbType = DbType.Leveldb;
            if (dbTypeString == DbType.RocksDb.ToString().ToLowerInvariant())
                dbType = DbType.RocksDb;

            return dbType;
        }
    }
}
