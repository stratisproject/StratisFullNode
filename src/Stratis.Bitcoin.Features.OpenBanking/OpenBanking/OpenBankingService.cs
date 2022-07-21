using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Database;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.OpenBanking.OpenBanking
{
    /// <summary>
    /// Buffered access to the Open Bank API.
    /// </summary>
    public class OpenBankingService : IOpenBankingService
    {
        private const byte commonTableOffset = 0;
        private const byte depositTableOffset = 64;
        private const byte indexTableOffset = 128;

        private readonly IDb db;
        private readonly DBreezeSerializer dBreezeSerializer;
        private readonly Network network;
        private readonly ChainIndexer chainIndexer;
        private readonly IPooledTransaction pooledTransaction;
        private readonly IOpenBankingClient openBankingClient;

        private readonly object lockObject = new object();

        public OpenBankingService(DataFolder dataFolder, DBreezeSerializer dBreezeSerializer, Network network, ChainIndexer chainIndexer, IPooledTransaction pooledTransaction, IOpenBankingClient openBankingClient)
        {
            this.dBreezeSerializer = dBreezeSerializer;
            this.network = network;
            this.chainIndexer = chainIndexer;
            this.pooledTransaction = pooledTransaction;
            this.openBankingClient = openBankingClient;

            this.db = new LevelDb();
            this.db.Open(Path.Combine(dataFolder.RootPath, "deposits"));
        }

        private OpenBankDeposit GetLastDeposit(IOpenBankAccount openBankAccount)
        {
            OpenBankDeposit lastDeposit = null;

            foreach (OpenBankDepositState state in typeof(OpenBankDepositState).GetEnumValues())
            {
                OpenBankDeposit deposit = GetOpenBankDeposits(openBankAccount, state).FirstOrDefault();

                // Ignore dates associated with pending deposits as the booking date time would be an estimate.
                if (deposit.State == OpenBankDepositState.Pending)
                    continue;

                if (deposit != null && (lastDeposit == null || lastDeposit.BookDateTimeUTC < deposit.BookDateTimeUTC))
                {
                    lastDeposit = deposit;
                }
            }

            return lastDeposit;
        }

        public void UpdateDeposits(IOpenBankAccount openBankAccount)
        {
            lock (this.lockObject)
            {
                OpenBankDeposit lastDeposit = GetLastDeposit(openBankAccount);

                using (var batch = this.db.GetWriteBatch())
                {
                    // Use the OpenBank API to add any deposits following the last deposit in the bank account.
                    foreach (OpenBankDeposit deposit in this.openBankingClient.GetDeposits(openBankAccount, lastDeposit?.BookDateTimeUTC))
                    {
                        // Add it if it does not already exist.
                        var existingDeposit = this.GetOpenBankDeposit(openBankAccount, deposit.ExternalId);
                        if (existingDeposit != null)
                        {
                            if (existingDeposit.State != OpenBankDepositState.Pending)
                            {
                                continue;
                            }

                            this.DeleteOpenBankDeposit(batch, openBankAccount, deposit);
                        }

                        this.PutOpenBankDeposit(batch, openBankAccount, deposit);
                    }

                    // Update pending deposits to "Booked" as required.
                    // This code nay not be required if pending deposits transit to booked with a booking date after the last deposit - i.e. if the date is like a "last updated" date.
                    foreach (OpenBankDeposit deposit in GetOpenBankDeposits(openBankAccount, OpenBankDepositState.Pending))
                    {
                        // TODO.
                    }

                    batch.Write();
                }
            }
        }

        public void UpdateDepositStatus(IOpenBankAccount openBankAccount)
        {
            lock (this.lockObject)
            {
                using (var batch = this.db.GetWriteBatch())
                {
                    bool isDirty = false;

                    // Look for any minting transactions that may no longer be present in blocks due to a re-org.
                    double maxReorgSeconds = this.network.Consensus.TargetSpacing.TotalSeconds * this.network.Consensus.MaxReorgLength;
                    DateTime fromDateUTC = DateTime.Now.AddSeconds(-maxReorgSeconds);

                    foreach (OpenBankDeposit deposit in GetOpenBankDeposits(openBankAccount, OpenBankDepositState.SeenInBlock))
                    {
                        if (deposit.BookDateTimeUTC < fromDateUTC)
                            break;

                        if (this.chainIndexer[deposit.Block.Hash] != null)
                            continue;

                        DeleteOpenBankDeposit(batch, openBankAccount, deposit);
                        deposit.State = OpenBankDepositState.Minted;
                        PutOpenBankDeposit(batch, openBankAccount, deposit);

                        isDirty = true;
                    }

                    // Look for any minting transactions that should be in the pool but are not.
                    foreach (OpenBankDeposit deposit in GetOpenBankDeposits(openBankAccount, OpenBankDepositState.Minted))
                    {
                        if (this.pooledTransaction.GetTransaction(deposit.TxId) != null)
                            continue;

                        DeleteOpenBankDeposit(batch, openBankAccount, deposit);
                        deposit.State = OpenBankDepositState.Booked;
                        PutOpenBankDeposit(batch, openBankAccount, deposit);

                        isDirty = true;
                    }

                    // Look for any minting transactions that have been added to the pool.
                    foreach (OpenBankDeposit deposit in GetOpenBankDeposits(openBankAccount, OpenBankDepositState.Booked))
                    {
                        if (this.pooledTransaction.GetTransaction(deposit.TxId) == null)
                            continue;

                        DeleteOpenBankDeposit(batch, openBankAccount, deposit);
                        deposit.State = OpenBankDepositState.Minted;
                        PutOpenBankDeposit(batch, openBankAccount, deposit);

                        isDirty = true;
                    }

                    if (isDirty)
                    {
                        batch.Write();
                    }
                }
            }
        }

        public void SetTransactionId(IOpenBankAccount openBankAccount, OpenBankDeposit deposit, uint256 txId)
        {
            lock (this.lockObject)
            {
                using (var batch = this.db.GetWriteBatch())
                {
                    deposit.TxId = txId;
                    PutOpenBankDeposit(batch, openBankAccount, deposit);
                    batch.Write();
                }
            }
        }

        private void DeleteOpenBankDeposit(IDbBatch batch, IOpenBankAccount openBankAccount, OpenBankDeposit deposit)
        {
            var depositTable = (byte)(depositTableOffset + openBankAccount.MetaDataTrackerEnum);
            var indexTable = (byte)(indexTableOffset + openBankAccount.MetaDataTrackerEnum);

            batch.Delete(depositTable, deposit.KeyBytes);
            batch.Delete(indexTable, deposit.IndexKeyBytes);
        }

        private void PutOpenBankDeposit(IDbBatch batch, IOpenBankAccount openBankAccount, OpenBankDeposit deposit)
        {
            var depositTable = (byte)(depositTableOffset + openBankAccount.MetaDataTrackerEnum);
            var indexTable = (byte)(indexTableOffset + openBankAccount.MetaDataTrackerEnum);

            batch.Put(depositTable, deposit.KeyBytes, this.dBreezeSerializer.Serialize(deposit));
            batch.Put(indexTable, deposit.IndexKeyBytes, new byte[0]);
        }

        public OpenBankDeposit GetOpenBankDeposit(IOpenBankAccount openBankAccount, string externalId)
        {
            var depositTable = (byte)(depositTableOffset + openBankAccount.MetaDataTrackerEnum);
            byte[] bytes = this.db.Get(depositTable, ASCIIEncoding.ASCII.GetBytes(externalId));
            if (bytes == null)
                return null;

            return this.dBreezeSerializer.Deserialize<OpenBankDeposit>(bytes);
        }

        public IEnumerable<OpenBankDeposit> GetOpenBankDeposits(IOpenBankAccount openBankAccount, OpenBankDepositState state)
        {
            var depositTable = (byte)(depositTableOffset + openBankAccount.MetaDataTrackerEnum);
            var indexTable = (byte)(indexTableOffset + openBankAccount.MetaDataTrackerEnum);

            // Iterate in reverse by External Id while the deposit's date >= fromDateUTC.
            using (var iterator = this.db.GetIterator(indexTable))
            {
                foreach ((byte[] key, _) in iterator.GetAll(ascending: false, firstKey: new byte[] { (byte)state }, lastKey: new byte[] { (byte)(1 + state) }, includeLastKey: false, keysOnly: true))
                {
                    var bytes = this.db.Get(depositTable, key.Skip(1).ToArray());
                    var deposit = this.dBreezeSerializer.Deserialize<OpenBankDeposit>(bytes);

                    yield return deposit;
                }
            }
        }
    }
}
