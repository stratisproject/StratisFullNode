using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.EventBus.CoreEvents;
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
        private readonly object lockObject = new object();

        private readonly ILogger logger;
        private readonly HashSet<OutPoint> reservedCoins = new HashSet<OutPoint>();

        public ReserveUtxoService(ILoggerFactory loggerFactory, ISignals signals)
        {
            this.logger = loggerFactory.CreateLogger(GetType().FullName);
            signals.Subscribe<BlockConnected>(OnBlockConnected);
            signals.Subscribe<TransactionFailedMempoolValidation>(OnTransactionFailedMempoolValidation);
        }

        private void OnBlockConnected(BlockConnected e)
        {
            lock (this.lockObject)
            {
                foreach (var input in e.ConnectedBlock.Block.Transactions.SelectMany(t => t.Inputs))
                {
                    this.logger.LogDebug("Tx added to block and removed from mempool, unreserving Utxo '{0}'", input.PrevOut.Hash);
                    this.reservedCoins.Remove(input.PrevOut);
                }
            }
        }

        private void OnTransactionFailedMempoolValidation(TransactionFailedMempoolValidation e)
        {
            lock (this.lockObject)
            {
                foreach (var input in e.Transaction.Inputs)
                {
                    this.logger.LogDebug("Tx failed mempool validation, unreserving Utxo '{0}' for transaction '{1}'", input.PrevOut.Hash, e.Transaction.GetHash());
                    this.reservedCoins.Remove(input.PrevOut);
                }
            }
        }

        public bool IsUtxoReserved(OutPoint outPoint)
        {
            lock (this.lockObject)
            {
                var result = this.reservedCoins.Contains(outPoint);
                this.logger.LogDebug("Outpoint '{0}' reserved = {1}", outPoint.Hash, result);
                return result;
            }
        }

        public void ReserveUtxos(IEnumerable<OutPoint> outPoints)
        {
            lock (this.lockObject)
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
}