using System;
using System.Collections.Generic;

namespace Stratis.Bitcoin.Features.Consensus.CoinViews
{
    public interface IDb : IDisposable
    {
        byte[] Get(byte table, byte[] key);

        IEnumerable<(byte[], byte[])> GetAll(byte table, bool keysOnly = false, bool ascending = true,
            byte[] firstKey = null, byte[] lastKey = null, bool includeFirstKey = true, bool includeLastKey = true);

        IDbBatch GetWriteBatch();

        void Clear();
    }

    public interface IDbBatch : IDisposable
    {
        IDbBatch Put(byte table, byte[] key, byte[] value);

        IDbBatch Delete(byte table, byte[] key);

        void Write();
    }
}
