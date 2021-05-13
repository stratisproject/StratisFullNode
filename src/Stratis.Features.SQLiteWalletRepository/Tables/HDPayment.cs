using System.Collections.Generic;
using System.Linq;
using SQLite;

namespace Stratis.Features.SQLiteWalletRepository.Tables
{
    internal class HDPayment
    {
        public long OutputTxTime { get; set; }
        public string OutputTxId { get; set; }
        public int OutputIndex { get; set; }
        public int SpendIndex { get; set; }
        public string SpendScriptPubKey { get; set; }
        public long SpendValue { get; set; }
        public int SpendIsChange { get; set; }

        internal static IEnumerable<string> CreateScript()
        {
            yield return $@"
            CREATE TABLE HDPayment (
                SpendTxTime         INTEGER NOT NULL,
                SpendTxId           TEXT NOT NULL,
                OutputTxId          TEXT NOT NULL,
                OutputIndex         INTEGER NOT NULL,
                ScriptPubKey        TEXT NOT NULL,
                SpendIndex          INTEGER NOT NULL,
                SpendScriptPubKey   TEXT,
                SpendValue          INTEGER NOT NULL,
                SpendIsChange       INTEGER NOT NULL,
                PRIMARY KEY(SpendTxId, OutputTxId, OutputIndex, ScriptPubKey, SpendIndex)
            )";
        }

        internal static IEnumerable<string> MigrateScript()
        {
            yield return $@"
                PRAGMA foreign_keys=off;
            ";

            yield return CreateScript().First().Replace("HDPayment", "new_HDPayment");

            // TODO: Copy the data.
            yield return $@"
                INSERT INTO new_HDPayment SELECT MAX(SpendTxTime), SpendTxId, OutputTxId, OutputIndex, ScriptPubKey, SpendIndex, MAX(SpendScriptPubKey), MAX(SpendValue), MAX(SpendIsChange) FROM HDPayment GROUP BY SpendTxId, OutputTxId, OutputIndex, ScriptPubKey, SpendIndex;
            ";

            yield return $@"
                DROP TABLE HDPayment;
            ";

            yield return $@"
                ALTER TABLE new_HDPayment RENAME TO HDPayment;
            ";

            foreach (var indexScript in CreateScript().Skip(1))
                yield return indexScript;

            yield return $@"
                PRAGMA foreign_keys=on;
            ";
        }

        internal static void CreateTable(SQLiteConnection conn)
        {
            foreach (string command in CreateScript())
                conn.Execute(command);
        }

        internal static void MigrateTable(SQLiteConnection conn)
        {
            if (conn.ExecuteScalar<int>("SELECT COUNT(*) AS CNTREC FROM pragma_table_info('HDPayment') WHERE name='SpendTxTime' AND pk = 1") != 0)
                foreach (string command in MigrateScript())
                    conn.Execute(command);
        }

        internal static IEnumerable<HDPayment> GetAllPayments(DBConnection conn, string spendTxId, string outputTxId, int outputIndex, string scriptPubKey)
        {
            return conn.Query<HDPayment>($@"
                SELECT  *
                FROM    HDPayment
                WHERE   SpendTxId = ?
                AND     OutputTxId = ?
                AND     OutputIndex = ?
                AND     ScriptPubKey = ?
                ORDER   BY SpendIndex",
                spendTxId,
                outputTxId,
                outputIndex,
                scriptPubKey);
        }

        internal static IEnumerable<HDPayment> GetPaymentsForTransactionId(DBConnection conn, string spendTxId)
        {
            return conn.Query<HDPayment>($@"
                SELECT  *
                FROM    HDPayment
                WHERE   SpendTxId = ?", spendTxId);
        }
    }
}
