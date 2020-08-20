using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.Wallet.Services;
using Stratis.Bitcoin.Networks;
using Xunit;

namespace Stratis.Bitcoin.Features.Wallet.Tests
{
    public sealed class ReserveUtxoServiceTests
    {
        private readonly ILoggerFactory loggerFactory;
        private readonly Signals.Signals signals;
        private readonly Network network;

        public ReserveUtxoServiceTests()
        {
            this.loggerFactory = new ExtendedLoggerFactory();
            this.signals = new Signals.Signals(this.loggerFactory, null);
            this.network = new StratisTest();
        }

        [Fact]
        public void CanReserveUtxo()
        {
            var service = new ReserveUtxoService(this.loggerFactory, this.signals);
            var outpoint = new OutPoint(new uint256(0), 1);
            service.ReserveUtxos(new[] { outpoint });
            Assert.True(service.IsUtxoReserved(outpoint));
        }

        [Fact]
        public void CanUnReserveUtxo()
        {
            var transaction = this.network.CreateTransaction();
            var outpoint = new OutPoint(new uint256(0), 1);
            transaction.AddInput(new TxIn(outpoint));

            var service = new ReserveUtxoService(this.loggerFactory, this.signals);
            service.ReserveUtxos(new[] { outpoint });
            Assert.True(service.IsUtxoReserved(outpoint));

            this.signals.Publish(new TransactionAddedToMemoryPool(transaction));
            Assert.False(service.IsUtxoReserved(outpoint));
        }
    }
}