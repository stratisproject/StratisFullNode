using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Database;
using Stratis.Bitcoin.Features.SmartContracts.MetadataTracker;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.OpenBanking.OpenBanking
{
    /// <summary>
    /// Buffered access to the Open Bank API.
    /// </summary>
    public class OpenBankingService : IOpenBankingService
    {
        private const byte depositTableOffset = 64;
        private const byte indexTableOffset = 128;

        private readonly IDb db;
        private readonly DBreezeSerializer dBreezeSerializer;
        private readonly Network network;
        private readonly ChainIndexer chainIndexer;
        private readonly IPooledTransaction pooledTransaction;
        private readonly IOpenBankingClient openBankingClient;
        private readonly IMetadataTracker metadataTracker;

        private readonly object lockObject = new object();

        public OpenBankingService(DataFolder dataFolder, DBreezeSerializer dBreezeSerializer, Network network, ChainIndexer chainIndexer, IPooledTransaction pooledTransaction, IOpenBankingClient openBankingClient, IMetadataTracker metadataTracker)
        {
            this.dBreezeSerializer = dBreezeSerializer;
            this.network = network;
            this.chainIndexer = chainIndexer;
            this.pooledTransaction = pooledTransaction;
            this.openBankingClient = openBankingClient;
            this.metadataTracker = metadataTracker;

            this.db = new LevelDb();
            this.db.Open(Path.Combine(dataFolder.RootPath, "deposits"));
        }

        private OpenBankDeposit GetLastDeposit(IOpenBankAccount openBankAccount)
        {
            OpenBankDeposit lastDeposit = null;

            foreach (OpenBankDepositState state in typeof(OpenBankDepositState).GetEnumValues())
            {
                // Ignore dates associated with pending deposits as the booking date time would/may be an estimate.
                if (state == OpenBankDepositState.Pending || state == OpenBankDepositState.Unknown)
                    continue;

                OpenBankDeposit deposit = GetOpenBankDeposits(openBankAccount, state).FirstOrDefault();

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
                    bool dirty = false;

                    // Use the OpenBank API to add any deposits following the last deposit in the bank account.
                    foreach (OpenBankDeposit deposit in this.openBankingClient.GetTransactions(openBankAccount, lastDeposit?.BookDateTimeUTC).GetDeposits(openBankAccount.Currency, this.network))
                    {
                        // See if a "Booked" or "Error" deposit is replacing a pending deposit
                        if ((deposit.State == OpenBankDepositState.Booked || deposit.State == OpenBankDepositState.Error))
                        {
                            var pendingDeposit = this.GetOpenBankDeposit(openBankAccount, deposit.PendingKeyBytes);
                            if (pendingDeposit != null)
                                this.DeleteOpenBankDeposit(batch, openBankAccount, pendingDeposit);
                        }

                        // Already exists?
                        var existingDeposit = this.GetOpenBankDeposit(openBankAccount, deposit.KeyBytes);
                        if (existingDeposit != null)
                            continue;

                        this.PutOpenBankDeposit(batch, openBankAccount, deposit);
                        dirty = true;
                    }

                    if (dirty)
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

                        // If its seen  in a block then set the state accordingly.
                        MetadataTrackerEntry entry = this.metadataTracker.GetEntryByMetadata(openBankAccount.MetaDataTrackerEnum, Encoders.ASCII.EncodeData(deposit.KeyBytes));
                        if (entry != null)
                        {
                            DeleteOpenBankDeposit(batch, openBankAccount, deposit);
                            deposit.Block = entry.Block;
                            deposit.State = OpenBankDepositState.SeenInBlock;
                            PutOpenBankDeposit(batch, openBankAccount, deposit);

                            isDirty = true;
                            continue;
                        }

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

        public OpenBankDeposit GetOpenBankDeposit(IOpenBankAccount openBankAccount, byte[] keyBytes)
        {
            var depositTable = (byte)(depositTableOffset + openBankAccount.MetaDataTrackerEnum);
            byte[] bytes = this.db.Get(depositTable, keyBytes);
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
