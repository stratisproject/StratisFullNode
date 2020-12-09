using System;
using System.Collections.Generic;
using LevelDB;

namespace Stratis.Bitcoin.Utilities
{
    public static class DBH
    {
        public static byte[] Key(byte table, byte[] key)
        {
            var dbkey = new byte[key.Length + 1];
            dbkey[0] = table;
            key.AsSpan().CopyTo(dbkey[1..]);

            return dbkey;
        }

        public static byte[] Key(byte table, ReadOnlySpan<byte> key)
        {
            var dbkey = new byte[key.Length + 1];
            dbkey[0] = table;
            key.CopyTo(dbkey[1..]);

            return dbkey;
        }

        public static Dictionary<byte[], byte[]> SelectDictionary(this DB db, byte table)
        {
            var dict = new Dictionary<byte[], byte[]>();

            var enumerator = db.GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (enumerator.Current.Key[0] == table)
                    dict.Add(enumerator.Current.Key.AsSpan().Slice(1).ToArray(), enumerator.Current.Value);
            }

            return dict;
        }
    }
}
