﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using LiteDB;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Database;
using Stratis.Bitcoin.Features.BlockStore.AddressIndexing;
using Stratis.Bitcoin.Features.BlockStore.Repositories;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Features.Consensus.Rules;
using Stratis.Bitcoin.Features.Consensus.Rules.CommonRules;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Utilities;
using Xunit;
using FileMode = LiteDB.FileMode;
using Script = NBitcoin.Script;

namespace Stratis.Bitcoin.Features.BlockStore.Tests
{
    public class AddressIndexerTests
    {
        private readonly IAddressIndexer addressIndexer;

        private readonly Mock<IConsensusManager> consensusManagerMock;

        private readonly ChainIndexer chainIndexer;

        private readonly Network network;

        private readonly IConsensusRuleEngine consensusRuleEngine;

        private readonly ChainedHeader genesisHeader;

        public AddressIndexerTests()
        {
            this.network = new StraxMain();
            this.chainIndexer = new ChainIndexer(this.network);
            var nodeSettings = new NodeSettings(this.network, args: new[] { "-addressindex", "-txindex" });

            var mockingServices = new ServiceCollection()
                .AddSingleton(this.network)
                .AddSingleton(nodeSettings)
                .AddSingleton(nodeSettings.LoggerFactory)
                .AddSingleton(new DataFolder(TestBase.CreateTestDir(this)))
                .AddSingleton<IScriptAddressReader, ScriptAddressReader>()
                .AddSingleton<ConsensusRulesContainer>()
                .AddSingleton<IConsensusRuleEngine, PosConsensusRuleEngine>()
                .AddSingleton<IBlockRepository>(typeof(BlockRepository<LevelDb>).GetConstructors().First(c => c.GetParameters().Any(p => p.ParameterType == typeof(DataFolder))))
                .AddSingleton<IBlockStore, BlockStoreQueue>()
                .AddSingleton<ICoindb>(typeof(Coindb<LevelDb>).GetConstructors().First(c => c.GetParameters().Any(p => p.ParameterType == typeof(DataFolder))))
                .AddSingleton<ICoinView, CachedCoinView>()
                .AddSingleton(this.chainIndexer)
                .AddSingleton<IDateTimeProvider, DateTimeProvider>()
                .AddSingleton<IAddressIndexer, AddressIndexerCV>();
            
            var mockingContext = new MockingContext(mockingServices);

            this.addressIndexer = mockingContext.GetService<IAddressIndexer>();
            this.genesisHeader = mockingContext.GetService<ChainIndexer>().GetHeader(0);

            var rulesContainer = mockingContext.GetService<ConsensusRulesContainer>();
            rulesContainer.FullValidationRules.Add(Activator.CreateInstance(typeof(LoadCoinviewRule)) as FullValidationConsensusRule);
            rulesContainer.FullValidationRules.Add(Activator.CreateInstance(typeof(SaveCoinviewRule)) as FullValidationConsensusRule);
            rulesContainer.FullValidationRules.Add(Activator.CreateInstance(typeof(StraxCoinviewRule)) as FullValidationConsensusRule);
            rulesContainer.FullValidationRules.Add(Activator.CreateInstance(typeof(SetActivationDeploymentsFullValidationRule)) as FullValidationConsensusRule);

            this.consensusManagerMock = mockingContext.GetService<Mock<IConsensusManager>>();
            this.consensusRuleEngine = mockingContext.GetService<IConsensusRuleEngine>();
        }

        [Fact]
        public void CanInitializeAndDispose()
        {
            this.consensusManagerMock.Setup(x => x.Tip).Returns(() => this.genesisHeader);

            this.addressIndexer.Initialize();
            this.addressIndexer.Dispose();
        }

        [Fact]
        public void CanIndexAddresses()
        {
            List<ChainedHeader> headers = ChainedHeadersHelper.CreateConsecutiveHeaders(100, null, false, null, this.network, chainIndexer: this.chainIndexer);
            this.consensusManagerMock.Setup(x => x.Tip).Returns(() => headers.Last());

            Script p2pk1 = this.GetRandomP2PKScript(out string address1);
            Script p2pk2 = this.GetRandomP2PKScript(out string address2);

            var block1 = new Block()
            {
                Transactions = new List<Transaction>()
                {
                    new Transaction()
                    {
                        Outputs =
                        {
                            new TxOut(new Money(10_000), p2pk1),
                            new TxOut(new Money(20_000), p2pk1),
                            new TxOut(new Money(30_000), p2pk1)
                        }
                    }
                }
            };

            var block5 = new Block()
            {
                Transactions = new List<Transaction>()
                {
                    new Transaction()
                    {
                        Outputs =
                        {
                            new TxOut(new Money(10_000), p2pk1),
                            new TxOut(new Money(1_000), p2pk2),
                            new TxOut(new Money(1_000), p2pk2)
                        }
                    }
                }
            };

            var tx = new Transaction();
            tx.Inputs.Add(new TxIn(new OutPoint(block5.Transactions.First().GetHash(), 0)));
            var block10 = new Block() { Transactions = new List<Transaction>() { tx } };

            ChainedHeaderBlock GetChainedHeaderBlock(uint256 hash)
            {
                ChainedHeader header = headers.SingleOrDefault(x => x.HashBlock == hash);

                switch (header?.Height)
                {
                    case 1:
                        return new ChainedHeaderBlock(block1, header);

                    case 5:
                        return new ChainedHeaderBlock(block5, header);

                    case 10:
                        return new ChainedHeaderBlock(block10, header);
                }

                return new ChainedHeaderBlock(new Block(), header);
            }

            this.consensusManagerMock.Setup(x => x.GetBlockData(It.IsAny<uint256>())).Returns((uint256 hash) =>
            {
                return GetChainedHeaderBlock(hash);
            });

            this.consensusManagerMock.Setup(x => x.GetBlockData(It.IsAny<List<uint256>>())).Returns((List<uint256> hashes) =>
            {
                return hashes.Select(h => GetChainedHeaderBlock(h)).ToArray();
            });

            this.consensusManagerMock.Setup(x => x.GetBlocksAfterBlock(It.IsAny<ChainedHeader>(), It.IsAny<int>(), It.IsAny<CancellationTokenSource>())).Returns((ChainedHeader header, int size, CancellationTokenSource token) =>
            {
                return headers.Where(h => h.Height > header.Height).Select(h => GetChainedHeaderBlock(h.HashBlock)).ToArray();
            });

            this.consensusManagerMock.Setup(x => x.ConsensusRules).Returns(this.consensusRuleEngine);

            this.consensusRuleEngine.Initialize(headers.Last(), this.consensusManagerMock.Object);
            this.addressIndexer.Initialize();

            TestBase.WaitLoop(() => this.addressIndexer.IndexerTip == headers.Last());

            Assert.Equal(60_000, this.addressIndexer.GetAddressBalances(new[] { address1 }).Balances.First().Balance.Satoshi);
            Assert.Equal(2_000, this.addressIndexer.GetAddressBalances(new[] { address2 }).Balances.First().Balance.Satoshi);

            Assert.Equal(70_000, this.addressIndexer.GetAddressBalances(new[] { address1 }, 93).Balances.First().Balance.Satoshi);

            // Now trigger rewind to see if indexer can handle reorgs.
            ChainedHeader forkPoint = headers.Single(x => x.Height == 8);

            List<ChainedHeader> headersFork = ChainedHeadersHelper.CreateConsecutiveHeaders(100, forkPoint, false, null, this.network, chainIndexer: this.chainIndexer);

            this.consensusManagerMock.Setup(x => x.GetBlockData(It.IsAny<uint256>())).Returns((uint256 hash) =>
            {
                ChainedHeader headerFork = headersFork.SingleOrDefault(x => x.HashBlock == hash);

                return new ChainedHeaderBlock(new Block(), headerFork);
            });

            this.consensusManagerMock.Setup(x => x.GetBlocksAfterBlock(It.IsAny<ChainedHeader>(), It.IsAny<int>(), It.IsAny<CancellationTokenSource>())).Returns((ChainedHeader header, int size, CancellationTokenSource token) =>
            {
                return headersFork.Where(h => h.Height > header.Height).Select(h => new ChainedHeaderBlock(new Block(), h)).ToArray();
            });

            this.consensusManagerMock.Setup(x => x.Tip).Returns(() => headersFork.Last());

            TestBase.WaitLoop(() => this.addressIndexer.IndexerTip == headersFork.Last());

            Assert.Equal(70_000, this.addressIndexer.GetAddressBalances(new[] { address1 }).Balances.First().Balance.Satoshi);

            this.addressIndexer.Dispose();
        }

        private Script GetRandomP2PKScript(out string address)
        {
            var bytes = RandomUtils.GetBytes(33);
            bytes[0] = 0x02;

            Script script = new Script() + Op.GetPushOp(bytes) + OpcodeType.OP_CHECKSIG;

            PubKey[] destinationKeys = script.GetDestinationPublicKeys(this.network);
            address = destinationKeys[0].GetAddress(this.network).ToString();

            return script;
        }

        [Fact]
        public void OutPointCacheCanRetrieveExisting()
        {
            const string CollectionName = "DummyCollection";
            var dataFolder = new DataFolder(TestBase.CreateTestDir(this));
            string dbPath = Path.Combine(dataFolder.RootPath, CollectionName);
            FileMode fileMode = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? FileMode.Exclusive : FileMode.Shared;

            var database = new LiteDatabase(new ConnectionString() { Filename = dbPath, Mode = fileMode });
            var cache = new AddressIndexerOutpointsRepository(database);

            var outPoint = new OutPoint(uint256.Parse("0000af9ab2c8660481328d0444cf167dfd31f24ca2dbba8e5e963a2434cffa93"), 0);

            var data = new OutPointData() { Outpoint = outPoint.ToString(), ScriptPubKeyBytes = new byte[] { 0, 0, 0, 0 }, Money = Money.Coins(1) };

            cache.AddOutPointData(data);

            Assert.True(cache.TryGetOutPointData(outPoint, out OutPointData retrieved));

            Assert.NotNull(retrieved);
            Assert.Equal(outPoint.ToString(), retrieved.Outpoint);
        }

        [Fact]
        public void OutPointCacheCannotRetrieveNonexistent()
        {
            const string CollectionName = "DummyCollection";
            var dataFolder = new DataFolder(TestBase.CreateTestDir(this));
            string dbPath = Path.Combine(dataFolder.RootPath, CollectionName);
            FileMode fileMode = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? FileMode.Exclusive : FileMode.Shared;

            var database = new LiteDatabase(new ConnectionString() { Filename = dbPath, Mode = fileMode });
            var cache = new AddressIndexerOutpointsRepository(database);

            Assert.False(cache.TryGetOutPointData(new OutPoint(uint256.Parse("0000af9ab2c8660481328d0444cf167dfd31f24ca2dbba8e5e963a2434cffa93"), 1), out OutPointData retrieved));
            Assert.Null(retrieved);
        }

        [Fact]
        public void OutPointCacheEvicts()
        {
            const string CollectionName = "OutputsData";
            var dataFolder = new DataFolder(TestBase.CreateTestDir(this));
            string dbPath = Path.Combine(dataFolder.RootPath, CollectionName);
            FileMode fileMode = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? FileMode.Exclusive : FileMode.Shared;

            var database = new LiteDatabase(new ConnectionString() { Filename = dbPath, Mode = fileMode });
            var cache = new AddressIndexerOutpointsRepository(database, 2);

            Assert.Equal(0, cache.Count);
            Assert.Equal(0, database.GetCollection<OutPointData>(CollectionName).Count());

            var outPoint1 = new OutPoint(uint256.Parse("0000af9ab2c8660481328d0444cf167dfd31f24ca2dbba8e5e963a2434cffa93"), 1); ;
            var pair1 = new OutPointData() { Outpoint = outPoint1.ToString(), ScriptPubKeyBytes = new byte[] { 0, 0, 0, 0 }, Money = Money.Coins(1) };

            cache.AddOutPointData(pair1);

            Assert.Equal(1, cache.Count);
            Assert.Equal(0, database.GetCollection<OutPointData>(CollectionName).Count());

            var outPoint2 = new OutPoint(uint256.Parse("cf8ce1419bbc4870b7d4f1c084534d91126dd3283b51ec379e0a20e27bd23633"), 2); ;
            var pair2 = new OutPointData() { Outpoint = outPoint2.ToString(), ScriptPubKeyBytes = new byte[] { 1, 1, 1, 1 }, Money = Money.Coins(2) };

            cache.AddOutPointData(pair2);

            Assert.Equal(2, cache.Count);
            Assert.Equal(0, database.GetCollection<OutPointData>(CollectionName).Count());

            var outPoint3 = new OutPoint(uint256.Parse("126dd3283b51ec379e0a20e27bd23633cf8ce1419bbc4870b7d4f1c084534d91"), 3); ;
            var pair3 = new OutPointData() { Outpoint = outPoint3.ToString(), ScriptPubKeyBytes = new byte[] { 2, 2, 2, 2 }, Money = Money.Coins(3) };

            cache.AddOutPointData(pair3);

            Assert.Equal(2, cache.Count);

            // One of the cache items should have been evicted, and will therefore be persisted on disk.
            Assert.Equal(1, database.GetCollection<OutPointData>(CollectionName).Count());

            // The evicted item should be pair1.
            Assert.Equal(pair1.ScriptPubKeyBytes, database.GetCollection<OutPointData>(CollectionName).FindAll().First().ScriptPubKeyBytes);

            // It should still be possible to retrieve pair1 from the cache (it will pull it from disk).
            Assert.True(cache.TryGetOutPointData(outPoint1, out OutPointData pair1AfterEviction));

            Assert.NotNull(pair1AfterEviction);
            Assert.Equal(pair1.ScriptPubKeyBytes, pair1AfterEviction.ScriptPubKeyBytes);
            Assert.Equal(pair1.Money, pair1AfterEviction.Money);
        }

        [Fact]
        public void AddressCacheCanRetrieveExisting()
        {
            const string CollectionName = "DummyCollection";
            var dataFolder = new DataFolder(TestBase.CreateTestDir(this));
            string dbPath = Path.Combine(dataFolder.RootPath, CollectionName);
            FileMode fileMode = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? FileMode.Exclusive : FileMode.Shared;

            var database = new LiteDatabase(new ConnectionString() { Filename = dbPath, Mode = fileMode });
            var cache = new AddressIndexRepository(database);

            string address = "xyz";
            var balanceChanges = new List<AddressBalanceChange>();

            balanceChanges.Add(new AddressBalanceChange() { BalanceChangedHeight = 1, Deposited = true, Satoshi = 1 });

            var data = new AddressIndexerData() { Address = address, BalanceChanges = balanceChanges };

            cache.AddOrUpdate(data.Address, data, data.BalanceChanges.Count + 1);

            AddressIndexerData retrieved = cache.GetOrCreateAddress("xyz");

            Assert.NotNull(retrieved);
            Assert.Equal("xyz", retrieved.Address);
            Assert.Equal(1, retrieved.BalanceChanges.First().BalanceChangedHeight);
            Assert.True(retrieved.BalanceChanges.First().Deposited);
            Assert.Equal(1, retrieved.BalanceChanges.First().Satoshi);
        }

        [Fact]
        public void AddressCacheRetrievesBlankRecordForNonexistent()
        {
            const string CollectionName = "DummyCollection";
            var dataFolder = new DataFolder(TestBase.CreateTestDir(this));
            string dbPath = Path.Combine(dataFolder.RootPath, CollectionName);
            FileMode fileMode = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? FileMode.Exclusive : FileMode.Shared;

            var database = new LiteDatabase(new ConnectionString() { Filename = dbPath, Mode = fileMode });
            var cache = new AddressIndexRepository(database);

            AddressIndexerData retrieved = cache.GetOrCreateAddress("xyz");

            // A record will be returned with no balance changes associated, if it is new.
            Assert.NotNull(retrieved);
            Assert.Equal("xyz", retrieved.Address);
            Assert.Empty(retrieved.BalanceChanges);
        }

        [Fact]
        public void AddressCacheEvicts()
        {
            const string CollectionName = "AddrData";
            var dataFolder = new DataFolder(TestBase.CreateTestDir(this));
            string dbPath = Path.Combine(dataFolder.RootPath, CollectionName);
            FileMode fileMode = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? FileMode.Exclusive : FileMode.Shared;

            var database = new LiteDatabase(new ConnectionString() { Filename = dbPath, Mode = fileMode });
            var cache = new AddressIndexRepository(database, 4);

            // Recall, each index entry counts as 1 and each balance change associated with it is an additional 1.
            Assert.Equal(0, database.GetCollection<AddressIndexerData>(CollectionName).Count());

            string address1 = "xyz";
            var balanceChanges1 = new List<AddressBalanceChange>();
            balanceChanges1.Add(new AddressBalanceChange() { BalanceChangedHeight = 1, Deposited = true, Satoshi = 1 });
            var data1 = new AddressIndexerData() { Address = address1, BalanceChanges = balanceChanges1 };

            cache.AddOrUpdate(data1.Address, data1, data1.BalanceChanges.Count + 1);

            Assert.Equal(0, database.GetCollection<AddressIndexerData>(CollectionName).Count());

            string address2 = "abc";
            var balanceChanges2 = new List<AddressBalanceChange>();
            balanceChanges2.Add(new AddressBalanceChange() { BalanceChangedHeight = 2, Deposited = false, Satoshi = 2 });

            cache.AddOrUpdate(address2, new AddressIndexerData() { Address = address2, BalanceChanges = balanceChanges2 }, balanceChanges2.Count + 1);

            Assert.Equal(0, database.GetCollection<AddressIndexerData>(CollectionName).Count());

            string address3 = "def";
            var balanceChanges3 = new List<AddressBalanceChange>();
            balanceChanges3.Add(new AddressBalanceChange() { BalanceChangedHeight = 3, Deposited = true, Satoshi = 3 });
            cache.AddOrUpdate(address3, new AddressIndexerData() { Address = address3, BalanceChanges = balanceChanges3 }, balanceChanges3.Count + 1);

            // One of the cache items should have been evicted, and will therefore be persisted on disk.
            Assert.Equal(1, database.GetCollection<AddressIndexerData>(CollectionName).Count());

            // The evicted item should be data1.
            Assert.Equal(data1.Address, database.GetCollection<AddressIndexerData>(CollectionName).FindAll().First().Address);
            Assert.Equal(1, database.GetCollection<AddressIndexerData>(CollectionName).FindAll().First().BalanceChanges.First().BalanceChangedHeight);
            Assert.True(database.GetCollection<AddressIndexerData>(CollectionName).FindAll().First().BalanceChanges.First().Deposited);
            Assert.Equal(1, database.GetCollection<AddressIndexerData>(CollectionName).FindAll().First().BalanceChanges.First().Satoshi);

            // Check that the first address can still be retrieved, it should come from disk in this case.
            AddressIndexerData retrieved = cache.GetOrCreateAddress("xyz");

            Assert.NotNull(retrieved);
            Assert.Equal("xyz", retrieved.Address);
            Assert.Equal(1, retrieved.BalanceChanges.First().BalanceChangedHeight);
            Assert.True(retrieved.BalanceChanges.First().Deposited);
            Assert.Equal(1, retrieved.BalanceChanges.First().Satoshi);
        }

        [Fact]
        public void MaxReorgIsCalculatedProperly()
        {
            var btc = new BitcoinMain();

            int maxReorgBtc = AddressIndexerCV.GetMaxReorgOrFallbackMaxReorg(btc);

            Assert.Equal(maxReorgBtc, AddressIndexerCV.FallBackMaxReorg);

            var stratis = new StraxMain();

            int maxReorgStratis = AddressIndexerCV.GetMaxReorgOrFallbackMaxReorg(stratis);

            Assert.Equal(maxReorgStratis, (int)stratis.Consensus.MaxReorgLength);
        }
    }
}
