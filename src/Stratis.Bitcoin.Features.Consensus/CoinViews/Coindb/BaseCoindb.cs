using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.Consensus.CoinViews
{
    /// <summary>
    /// A base class for <see cref="LevelDbCoindb"/> and implementations related to other database types.
    /// </summary>
    public class BaseCoindb
    {
        protected static readonly byte coinsTable = 1;
        protected static readonly byte blockTable = 2;
        protected static readonly byte rewindTable = 3;
        protected static readonly byte stakeTable = 4;
        protected static readonly byte balanceTable = 5;
        protected static readonly byte balanceAdjustmentTable = 6;

        /// <summary>Access to dBreeze database.</summary>
        protected IDb coinDb;

        /// <summary>Hash of the block which is currently the tip of the coinview.</summary>
        protected HashHeightPair persistedCoinviewTip;

        private readonly IScriptAddressReader scriptAddressReader;

        private readonly Network network;

        public BaseCoindb(Network network, IScriptAddressReader scriptAddressReader)
        {
            this.network = network;
            this.scriptAddressReader = scriptAddressReader;
        }

        public IEnumerable<(uint height, long satoshis)> GetBalance(TxDestination txDestination)
        {
            long balance = 0;
            {
                byte[] row = this.coinDb.Get(balanceTable, txDestination.ToBytes());
                balance = (row == null) ? 0 : BitConverter.ToInt64(row);
            }

            foreach ((byte[] key, byte[] value) in this.coinDb.GetAll(balanceAdjustmentTable, ascending: false, 
                lastKey: txDestination.ToBytes().Concat(BitConverter.GetBytes(this.persistedCoinviewTip.Height + 1).Reverse()).ToArray(), 
                includeLastKey: false,
                firstKey: txDestination.ToBytes(),
                includeFirstKey: false))
            {
                yield return (BitConverter.ToUInt32(key.Reverse().ToArray()), balance);
                balance -= BitConverter.ToInt64(value);
            }

            yield return (0, balance);
        }

        /// <summary>
        /// The 'skipMissing' flag allows us to rewind coind db's that have incomplete balance information.
        /// </summary>
        protected void AdjustBalance(IDbBatch batch, Dictionary<TxDestination, Dictionary<uint, long>> balanceUpdates)
        {
            foreach ((TxDestination txDestination, Dictionary<uint, long> balanceAdjustments) in balanceUpdates)
            {
                long totalAdjustment = 0;

                foreach (uint height in balanceAdjustments.Keys.OrderBy(k => k))
                {
                    var key = txDestination.ToBytes().Concat(BitConverter.GetBytes(height).Reverse()).ToArray();
                    byte[] row = this.coinDb.Get(balanceAdjustmentTable, key);
                    long balance = ((row == null) ? 0 : BitConverter.ToInt64(row)) + balanceAdjustments[height];
                    batch.Put(balanceAdjustmentTable, key, BitConverter.GetBytes(balance));

                    totalAdjustment += balance;
                }

                {
                    var key = txDestination.ToBytes();
                    byte[] row = this.coinDb.Get(balanceTable, key);
                    long balance = ((row == null) ? 0 : BitConverter.ToInt64(row)) + totalAdjustment;
                    batch.Put(balanceTable, key, BitConverter.GetBytes(balance));
                }
            }
        }

        protected void Update(Dictionary<TxDestination, Dictionary<uint, long>> balanceAdjustments, Script scriptPubKey, uint height, long change)
        {
            if (scriptPubKey.Length == 0 || change == 0)
                return;

            foreach (TxDestination txDestination in this.scriptAddressReader.GetDestinationFromScriptPubKey(this.network, scriptPubKey))
            {
                if (!balanceAdjustments.TryGetValue(txDestination, out Dictionary<uint, long> value))
                {
                    value = new Dictionary<uint, long>();
                    balanceAdjustments[txDestination] = value;
                }

                if (!value.TryGetValue(height, out long balance))
                    balance = change;
                else
                    balance += change;

                value[height] = balance;
            }
        }
    }
}
