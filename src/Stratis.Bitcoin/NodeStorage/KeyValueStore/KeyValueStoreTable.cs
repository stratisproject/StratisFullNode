using Stratis.Bitcoin.Interfaces;

namespace Stratis.Bitcoin.NodeStorage.KeyValueStore
{
    public class KeyValueStoreTable
    {
        public string TableName { get; internal set; }

        public IKeyValueStoreRepository Repository { get; internal set; }
    }
}
