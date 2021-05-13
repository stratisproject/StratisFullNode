using System.Collections.Generic;
using SQLite;
using Stratis.Bitcoin.Features.Wallet;

namespace Stratis.Features.SQLiteWalletRepository.Tables
{
    internal class HDTransactionData
    {
        public int WalletId { get; set; }
        public int AccountIndex { get; set; }
        public int AddressType { get; set; }
        public int AddressIndex { get; set; }

        /// <summary>
        /// This is the exact bytes in the Script of {OutputTxId-OutputIndex}.
        /// It should probably be called ScriptPubKey!
        /// </summary>
        public string RedeemScript { get; set; }

        /// <summary>
        /// This is the derived ScriptPubKey from IScriptDestinationReader.
        /// It should probably be called AddressScriptPubKey.
        /// It will not always contain the actual bytes in the script of {OutputTxId-OutputIndex}.
        /// </summary>
        public string ScriptPubKey { get; set; }
        public string Address { get; set; }
        public long Value { get; set; }
        public long OutputTxTime { get; set; }
        public string OutputTxId { get; set; }
        public int OutputIndex { get; set; }
        public int? OutputBlockHeight { get; set; }
        public string OutputBlockHash { get; set; }
        public int OutputTxIsCoinBase { get; set; }
        public long? SpendTxTime { get; set; }
        public string SpendTxId { get; set; }
        public int? SpendBlockHeight { get; set; }
        public int SpendTxIsCoinBase { get; set; }
        public string SpendBlockHash { get; set; }
        public long? SpendTxTotalOut { get; set; }

        internal static IEnumerable<string> CreateScript()
        {
            yield return $@"
            CREATE TABLE HDTransactionData (
                WalletId            INTEGER NOT NULL,
                AccountIndex        INTEGER NOT NULL,
                AddressType         INTEGER NOT NULL,
                AddressIndex        INTEGER NOT NULL,
                RedeemScript        TEXT NOT NULL,
                ScriptPubKey        TEXT NOT NULL,
                Address             TEXT NOT NULL,
                Value               INTEGER NOT NULL,
                OutputBlockHeight   INTEGER,
                OutputBlockHash     TEXT,
                OutputTxIsCoinBase  INTEGER NOT NULL,
                OutputTxTime        INTEGER NOT NULL,
                OutputTxId          TEXT NOT NULL,
                OutputIndex         INTEGER NOT NULL,
                SpendBlockHeight    INTEGER,
                SpendBlockHash      TEXT,
                SpendTxIsCoinBase   INTEGER,
                SpendTxTime         INTEGER,
                SpendTxId           TEXT,
                SpendTxTotalOut     INTEGER,
                PRIMARY KEY(WalletId, AccountIndex, AddressType, AddressIndex, OutputTxId, OutputIndex)
            )";

            yield return "CREATE UNIQUE INDEX UX_HDTransactionData_Output ON HDTransactionData(WalletId, OutputTxId, OutputIndex, ScriptPubKey)";
            yield return "CREATE INDEX IX_HDTransactionData_SpendTxTime ON HDTransactionData (WalletId, AccountIndex, SpendTxTime)";
            yield return "CREATE INDEX IX_HDTransactionData_OutputTxTime ON HDTransactionData (WalletId, AccountIndex, OutputTxTime, OutputIndex)";
        }

        internal static void CreateTable(SQLiteConnection conn)
        {
            foreach (string command in CreateScript())
                conn.Execute(command);
        }

        internal static IEnumerable<HDTransactionData> GetAllTransactions(DBConnection conn, int walletId, int? accountIndex, int? addressType, int? addressIndex, int limit = int.MaxValue, HDTransactionData prev = null, bool descending = true)
        {
            string strWalletId = DBParameter.Create(walletId);
            string strAccountIndex = DBParameter.Create(accountIndex);
            string strAddressType = DBParameter.Create(addressType);
            string strAddressIndex = DBParameter.Create(addressIndex);
            string strLimit = DBParameter.Create(limit);
            string strPrevTime = DBParameter.Create(prev?.OutputTxTime);
            string strPrevIndex = DBParameter.Create(prev?.OutputIndex);

            return conn.Query<HDTransactionData>($@"
                SELECT  *
                FROM    HDTransactionData
                WHERE   WalletId = {strWalletId} {((accountIndex == null) ? $@"
                AND     AccountIndex IN (SELECT AccountIndex FROM HDAccount WHERE WalletId = {strWalletId})" : $@"
                AND     AccountIndex = {strAccountIndex}")} {((addressType == null) ? $@"
                AND     AddressType IN (0, 1)" : $@"
                AND     AddressType = {strAddressType}")} {((addressIndex == null) ? "" : $@"
                AND     AddressIndex = {strAddressIndex}")} {((prev == null) ? "" : (!descending ? $@"
                AND 	(OutputTxTime > {strPrevTime} OR (OutputTxTime = {strPrevTime} AND OutputIndex > {strPrevIndex}))" : $@"
                AND 	(OutputTxTime < {strPrevTime} OR (OutputTxTime = {strPrevTime} AND OutputIndex < {strPrevIndex}))"))} {(!descending ? $@"
                ORDER   BY WalletId, AccountIndex, OutputTxTime, OutputIndex" : $@"
                ORDER   BY WalletId DESC, AccountIndex DESC, OutputTxTime DESC, OutputIndex DESC")}
                LIMIT   {strLimit}");
        }

        internal static int GetTransactionCount(DBConnection conn, int walletId, int? accountIndex)
        {
            string strWalletId = DBParameter.Create(walletId);
            string strAccountIndex = DBParameter.Create(accountIndex);
            return conn.ExecuteScalar<int>($@"
                SELECT  Count (*)
                FROM    HDTransactionData
                WHERE   WalletId = {strWalletId} {(accountIndex == null ? $@"
                AND     AccountIndex IN (SELECT AccountIndex FROM HDAccount WHERE WalletId = {strWalletId})" : $@"
                AND     AccountIndex = {strAccountIndex}")}");
        }

        internal static IEnumerable<HDTransactionData> GetSpendableTransactions(DBConnection conn, int walletId, int accountIndex, int currentChainHeight, long coinbaseMaturity, int confirmations = 0)
        {
            int maxConfirmationHeight = (currentChainHeight + 1) - confirmations;
            // TODO: This value can go negative, which is a bit ugly
            int maxCoinBaseHeight = currentChainHeight - (int)coinbaseMaturity;

            string strWalletId = DBParameter.Create(walletId);
            string strAccountIndex = DBParameter.Create(accountIndex);
            string strMaxConfirmationHeight = DBParameter.Create(maxConfirmationHeight);
            string strMaxCoinBaseHeight = DBParameter.Create(maxCoinBaseHeight);

            return conn.Query<HDTransactionData>($@"
                SELECT  *
                FROM    HDTransactionData
                WHERE   (WalletId, AccountIndex) IN (SELECT {strWalletId}, {strAccountIndex})
                AND     SpendTxTime IS NULL {((confirmations == 0) ? "" : $@"
                AND     OutputBlockHeight <= {strMaxConfirmationHeight}")}
                AND     (OutputTxIsCoinBase = 0 OR OutputBlockHeight <= {strMaxCoinBaseHeight})
                AND     Value > 0
                ORDER   BY OutputBlockHeight
                ,       OutputTxId
                ,       OutputIndex");
        }

        public class BalanceData
        {
            public long TotalBalance { get; set; }
            public long SpendableBalance { get; set; }
            public long ConfirmedBalance { get; set; }
        }

        internal static (long total, long confirmed, long spendable) GetBalance(DBConnection conn, int walletId, int accountIndex, (int type, int index)? address, int currentChainHeight, int coinbaseMaturity, int confirmations = 0)
        {
            int maxConfirmationHeight = (currentChainHeight + 1) - confirmations;
            int maxCoinBaseHeight = currentChainHeight - (int)coinbaseMaturity;

            string strWalletId = DBParameter.Create(walletId);
            string strAccountIndex = DBParameter.Create(accountIndex);
            string strMaxConfirmationHeight = DBParameter.Create(maxConfirmationHeight);
            string strMaxCoinBaseHeight = DBParameter.Create(maxCoinBaseHeight);

            // If confirmations is 0, then we want no restrictions on the max confirmation height. Even NULL height is allowed. (aka unconfirmed).
            // IF confirmations is 1 or more, then we don't want NULL height and the restriction should be applied.
            string confirmationsQuery = confirmations == 0
                                        ? ""
                                        : $"OutputBlockHeight <= {strMaxConfirmationHeight} AND ";

            var balanceData = conn.FindWithQuery<BalanceData>($@"
                SELECT SUM(Value) TotalBalance
                ,      SUM(CASE WHEN {confirmationsQuery} (OutputTxIsCoinBase = 0 OR OutputBlockHeight <= {strMaxCoinBaseHeight})
                       THEN Value ELSE 0 END) SpendableBalance
                ,      SUM(CASE WHEN OutputBlockHeight IS NOT NULL THEN Value ELSE 0 END) ConfirmedBalance
                FROM   HDTransactionData
                WHERE  (WalletId, AccountIndex) IN (SELECT {strWalletId}, {strAccountIndex})
                AND    SpendTxTime IS NULL { ((address == null) ? "" : $@"
                AND    (AddressType, AddressIndex) IN (SELECT {DBParameter.Create(address?.type)}, {DBParameter.Create(address?.index)})")}
                AND    Value > 0");

            return (balanceData.TotalBalance, balanceData.ConfirmedBalance, balanceData.SpendableBalance);
        }

        // Retrieves a transaction by it's id.
        internal static IEnumerable<HDTransactionData> GetTransactionsById(DBConnection conn, int walletId, string transactionId)
        {
            string strTransactionId = DBParameter.Create(transactionId);
            string strWalletId = DBParameter.Create(walletId);

            return conn.Query<HDTransactionData>($@"
                SELECT  *
                FROM    HDTransactionData
                WHERE   WalletId = {strWalletId}
                AND     (OutputTxId = {strTransactionId} OR SpendTxId = {strTransactionId})");
        }

        // Finds account transactions acting as inputs to other wallet transactions - i.e. not a complete list of transaction inputs.
        internal static IEnumerable<HDTransactionData> FindTransactionInputs(DBConnection conn, int walletId, int? accountIndex, long? transactionTime, string transactionId)
        {
            string strWalletId = DBParameter.Create(walletId);
            string strAccountIndex = DBParameter.Create(accountIndex);
            string strTransactionTime = DBParameter.Create(transactionTime);
            string strTransactionId = DBParameter.Create(transactionId);

            return conn.Query<HDTransactionData>($@"
                SELECT  *
                FROM    HDTransactionData
                WHERE   WalletId = {strWalletId} {((accountIndex != null) ? $@"
                AND     AccountIndex = {strAccountIndex}" : $@"
                AND     AccountIndex IN (SELECT AccountIndex FROM HDAccount WHERE WalletId = {strWalletId})")} { ((transactionTime == null) ? "" : $@"
                AND     SpendTxTime = {strTransactionTime}")}
                AND     SpendTxId = {strTransactionId}");
        }

        // Finds the wallet transaction data related to a transaction - i.e. not a complete list of transaction outputs.
        internal static IEnumerable<HDTransactionData> FindTransactionOutputs(DBConnection conn, int walletId, int? accountIndex, long? transactionTime, string transactionId)
        {
            string strWalletId = DBParameter.Create(walletId);
            string strAccountIndex = DBParameter.Create(accountIndex);
            string strTransactionTime = DBParameter.Create(transactionTime);
            string strTransactionId = DBParameter.Create(transactionId);

            return conn.Query<HDTransactionData>($@"
                SELECT  *
                FROM    HDTransactionData
                WHERE   WalletId = {strWalletId} {((accountIndex != null) ? $@"
                AND     AccountIndex = {strAccountIndex}" : $@"
                AND     AccountIndex IN (SELECT AccountIndex FROM HDAccount WHERE WalletId = {strWalletId})")} { ((transactionTime == null) ? "" : $@"
                AND     OutputTxTime = {strTransactionTime}")}
                AND     OutputTxId = {strTransactionId}");
        }

        /// <summary>
        /// Returns a wallet transaction history items (paged or full).
        /// </summary>
        /// <param name="conn">Connection to the database engine.</param>
        /// <param name="walletId">The wallet we are retrieving history for.</param>
        /// <param name="accountIndex">The account index in question.</param>
        /// <returns>An unpaged set of wallet transaction history items</returns>
        internal static IEnumerable<FlattenedHistoryItem> GetHistory(DBConnection conn, int walletId, int accountIndex, int limit, int offset, string txId)
        {
            string strLimit = DBParameter.Create(limit);
            string strOffset = DBParameter.Create(offset);
            string strWalletId = DBParameter.Create(walletId);
            string strAccountIndex = DBParameter.Create(accountIndex);
            string strTransactionId = DBParameter.Create(txId);

            var result = conn.Query<FlattenedHistoryItem>($@"

            -- Interwoven receives and spends
            SELECT * FROM
            (
                -- Find all receives
                SELECT
                    t.WalletId as WalletId,
                    t.AccountIndex as AccountIndex,
                    t.OutputTxId as Id, 
                    t.RedeemScript,
                CASE 
                    WHEN t.OutputTxIsCoinbase = 0 AND t.AddressType = 0 THEN 0
                    WHEN t.OutputTxIsCoinbase = 1 AND t.OutputIndex = 0 THEN 3
                    WHEN t.OutputTxIsCoinbase = 1 AND t.OutputIndex != 0 THEN 2
                END Type,                    
                t.OutputTxTime as TimeStamp,
                CASE
                    WHEN t.OutputTxIsCoinbase = 0 AND t.AddressType = 0 THEN t.Value
                    WHEN t.OutputTxIsCoinbase = 0 AND t.AddressType = 1 THEN ((SELECT sum(tt.Value) FROM HDTransactionData tt WHERE tt.SpendTxId = t.OutputTxId) - t.Value)
                    WHEN t.OutputTxIsCoinbase = 1 AND t.OutputIndex = 0 THEN t.Value
                    WHEN t.OutputTxIsCoinbase = 1 AND t.OutputIndex != 0 THEN (SUM(t.Value) -
                    (
                        SELECT tt.Value
                        FROM HDPayment p
                        INNER JOIN HDTransactionData tt ON tt.OutputTxId = p.OutputTxId AND tt.OutputIndex = p.OutputIndex
                        WHERE p.SpendTxId = t.OutputTxId AND p.SpendIsChange = 0
                        LIMIT 1
                    ))
                END Amount,
                NULL as Fee,
                NULL as SendToScriptPubkey,
                t.Address AS ReceiveAddress,
                t.OutputBlockHeight as BlockHeight
              FROM 
                HDTransactionData AS t
              GROUP BY t.OutputTxId    
            UNION ALL
                SELECT * FROM 
                (
                    -- Find all sends
                    SELECT
                        t.WalletId as WalletId,        
                        t.AccountIndex as AccountIndex,
                        t.SpendTxId as Id,
                        t.RedeemScript,
                        1 as Type,
                        t.SpendTxTime as TimeStamp,
                        p.SendValue AS Amount,
                        t.Value - t.SpendTxTotalOut as Fee,
                        p.SpendScriptPubKey as SendToScriptPubkey,
                        NULL AS ReceiveAddress,
                        t.SpendBlockHeight as BlockHeight
                    FROM
                        (SELECT WalletId, AccountIndex, SpendTxId, SpendTxTime, SpendTxTotalOut, SUM(Value) Value, SpendBlockHeight, RedeemScript FROM HDTransactionData WHERE SpendtxId IS NOT NULL AND SpendTxIsCoinbase = 0 GROUP BY SpendTxId) t
                    LEFT JOIN (
                        SELECT SpendTxId
                    	,      SpendScriptPubKey
                    	,      SUM(SpendValue) SendValue
                    	FROM   (SELECT DISTINCT SpendTxId, SpendIndex, SpendValue, SpendScriptPubKey, SpendIsChange FROM HDPayment) p2
                    	LEFT   JOIN   HDAddress a
                    	ON     a.ScriptPubKey = p2.SpendScriptPubKey	
                    	WHERE  SpendIsChange = 0 AND a.ScriptPubKey IS NULL
                    	GROUP  BY SpendTxId, p2.SpendScriptPubKey
                    	) p
                    ON   p.SpendTxId = t.SpendTxId
                    GROUP BY t.SpendtxId, p.SpendScriptPubKey
                 )  
            ) as T
            WHERE
                T.Type IS NOT NULL --eliminate sends in the first UNION
                AND T.WalletId = {strWalletId} 
                AND T.AccountIndex = {strAccountIndex} {((txId == null) ? "" : $@" AND T.Id = {strTransactionId}")}
            ORDER BY
                T.TimeStamp DESC
            LIMIT {strLimit} OFFSET {strOffset}");

            return result;
        }
    }
}