using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Signals;

namespace Stratis.Bitcoin.Features.Wallet.Services
{
    public interface IReserveUtxoService
    {
        void ReserveUtxos(IEnumerable<OutPoint> outPoint);
        bool IsUtxoReserved(OutPoint outPoint);
    }

    public sealed class ReserveUtxoService : IReserveUtxoService
    {
        private readonly ILogger logger;
        private readonly HashSet<OutPoint> reservedCoins = new HashSet<OutPoint>();

        public ReserveUtxoService(ILoggerFactory loggerFactory, ISignals signals)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            signals.Subscribe<TransactionAddedToMemoryPool>(this.OnTransactionAdded);
        }

        private void OnTransactionAdded(TransactionAddedToMemoryPool tx)
        {
            this.logger.LogDebug("Unreserving UTXOs for transaction '{0}'", tx.AddedTransaction.GetHash());

            foreach (var input in tx.AddedTransaction.Inputs)
            {
                this.reservedCoins.Remove(input.PrevOut);
            }
        }

        public bool IsUtxoReserved(OutPoint outPoint)
        {
            var result = this.reservedCoins.Contains(outPoint);
            this.logger.LogDebug("Outpoint '{0}' reserved = {1}", outPoint.Hash, result);
            return result;
        }

        public void ReserveUtxos(IEnumerable<OutPoint> outPoints)
        {
            foreach (var outPoint in outPoints)
            {
                if (!this.reservedCoins.Contains(outPoint))
                {
                    this.reservedCoins.Add(outPoint);
                    this.logger.LogDebug("Reserving UTXO '{0}'", outPoint.Hash);
                }
            }
        }
    }
}