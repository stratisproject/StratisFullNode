using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using NBitcoin.DataEncoders;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Utilities;
using Xunit;

namespace Stratis.Bitcoin.Features.BlockStore.Tests
{
    public class BlockStoreTests
    {
        private BlockStoreQueue blockStoreQueue;
        private readonly IChainState chainState;
        private readonly Mock<IInitialBlockDownloadState> initialBlockDownloadState;
        private readonly NodeLifetime nodeLifetime;
        private ChainIndexer chainIndexer;
        private readonly Network network;
        private HashHeightPair repositoryTipHashAndHeight;
        private readonly Mock<IBlockRepository> blockRepositoryMock;
        private int repositorySavesCount = 0;
        private int repositoryTotalBlocksSaved = 0;
        private int repositoryTotalBlocksDeleted = 0;
        private readonly Random random;
        private readonly StoreSettings storeSettings;

        private readonly Dictionary<uint256, Block> listOfSavedBlocks;
        private readonly ILoggerFactory loggerFactory;
        private readonly ISignals signals;
        private readonly AsyncProvider asyncProvider;

        private readonly string testBlockHex = "0000002082d011586557647aa1461c214bedc0987cf8f7c5eb19662770c42593d058e1ebc41b5aabaee7c7db2fdb09fe1986635b9208d8b298610252840295766809f2ff6c26a95fffff0f1e667f000001010000000001010000000000000000000000000000000000000000000000000000000000000000ffffffff025100ffffffff0200d2496b00000000232103186e88ef2ca4684befe5028c270783fe2d2f9e4a866afd37faa44ce0bbb7c2afac0000000000000000266a24aa21a9ede2f61c3f71d1defd3fa999dfa36953755c690689799962b48bebd836974e8cf9012000000000000000000000000000000000000000000000000000000000000000000000000000";

        public BlockStoreTests()
        {
            this.network = new StraxMain();
            this.repositoryTipHashAndHeight = new HashHeightPair(this.network.GenesisHash, 0);

            var nodeSettings = new NodeSettings(this.network, args: new [] { $"-datadir={TestBase.GetTestDirectoryPath(this)}" });

            this.storeSettings = new StoreSettings(nodeSettings);

            this.random = new Random();

            this.listOfSavedBlocks = new Dictionary<uint256, Block>
            {
                { uint256.One, Block.Parse(this.testBlockHex, this.network.Consensus.ConsensusFactory) }
            };

            this.chainIndexer = CreateChain(10);

            this.nodeLifetime = new NodeLifetime();

            this.blockRepositoryMock = new Mock<IBlockRepository>();

            this.blockRepositoryMock.Setup(x => x.TxIndex).Returns(this.storeSettings.TxIndex);

            this.blockRepositoryMock.Setup(x => x.PutBlocks(It.IsAny<HashHeightPair>(), It.IsAny<List<Block>>()))
                .Callback((HashHeightPair newTip, List<Block> blocks) =>
            {
                this.repositoryTipHashAndHeight = newTip;
                this.repositorySavesCount++;
                this.repositoryTotalBlocksSaved += blocks.Count;
            });

            this.blockRepositoryMock.Setup(x => x.Delete(It.IsAny<HashHeightPair>(), It.IsAny<List<uint256>>()))
                .Callback((HashHeightPair newTip, List<uint256> blocks) =>
            {
                this.repositoryTotalBlocksDeleted += blocks.Count;
            });

            this.blockRepositoryMock.Setup(x => x.GetBlock(It.IsAny<uint256>()))
                .Returns((uint256 hash) =>
            {
                Block block = null;

                if (this.listOfSavedBlocks.ContainsKey(hash))
                    block = this.listOfSavedBlocks[hash];

                return block;
            });

            this.blockRepositoryMock.Setup(x => x.TipHashAndHeight).Returns(() =>
            {
                return this.repositoryTipHashAndHeight;
            });

            this.chainState = new ChainState();
            this.initialBlockDownloadState = new Mock<IInitialBlockDownloadState>();

            var blockStoreFlushCondition = new BlockStoreQueueFlushCondition(this.chainState, this.initialBlockDownloadState.Object);

            this.loggerFactory = new LoggerFactory();
            this.signals = new Signals.Signals(this.loggerFactory, null);
            this.asyncProvider = new AsyncProvider(this.loggerFactory, this.signals);

            this.blockStoreQueue = new BlockStoreQueue(this.chainIndexer, this.chainState, blockStoreFlushCondition, this.storeSettings,
                this.blockRepositoryMock.Object, this.loggerFactory, new Mock<INodeStats>().Object, this.asyncProvider, new Mock<IInitialBlockDownloadState>().Object);
        }

        private ChainIndexer CreateChain(int blocksCount)
        {
            var chain = new ChainIndexer(this.network);
            for (int i = 0; i < blocksCount; i++)
            {
                BlockHeader header = this.network.Consensus.ConsensusFactory.CreateBlockHeader();
                header.Nonce = RandomUtils.GetUInt32();
                header.HashPrevBlock = chain.Tip.HashBlock;
                header.Bits = Target.Difficulty1;

                var chainedHeader = new ChainedHeader(header, header.GetHash(), chain.Tip);
                chain.SetTip(chainedHeader);
            }

            return chain;
        }

        private async Task WaitUntilQueueIsEmptyAsync()
        {
            int iterations = 0;

            var queue = this.blockStoreQueue.GetMemberValue("blocksQueue") as IAsyncQueue<ChainedHeaderBlock>;

            while (true)
            {
                int itemsCount = ((Queue<ChainedHeaderBlock>)queue.GetMemberValue("items")).Count;

                if (itemsCount != 0)
                    await Task.Delay(100).ConfigureAwait(false);
                else
                    break;

                iterations++;

                if (iterations > 500)
                    throw new Exception("Unexpected queue processing delay!");
            }

            // For very slow environments.
            await Task.Delay(500).ConfigureAwait(false);
        }

        [Fact]
        public void BlockStoreInitializesTipAtHashOfLastSavedBlock()
        {
            ChainedHeader initializationHeader = this.chainIndexer.Tip.Previous.Previous.Previous;
            this.repositoryTipHashAndHeight = new HashHeightPair(initializationHeader);

            this.blockStoreQueue.Initialize();
            Assert.Equal(initializationHeader, this.chainState.BlockStoreTip);
        }

        [Fact]
        public void BlockStoreRecoversToLastCommonBlockOnInitialization()
        {
            this.repositoryTipHashAndHeight = new HashHeightPair(uint256.One, 1);

            this.blockStoreQueue.Initialize();

            Assert.Equal(this.chainIndexer.Genesis, this.chainState.BlockStoreTip);
        }

        [Fact]
        public async Task BatchIsSavedAfterSizeThresholdReachedAsync()
        {
            Block block = Block.Load(Encoders.Hex.DecodeData(this.testBlockHex), this.network.Consensus.ConsensusFactory);
            int blockSize = block.GetSerializedSize();
            this.chainState.ConsensusTip = null;

            int count = 5 * 1024 * 1024 / blockSize + 2;

            ChainIndexer longChainIndexer = this.CreateChain(count);
            this.repositoryTipHashAndHeight = new HashHeightPair(longChainIndexer.Genesis.HashBlock, 0);

            var blockStoreFlushCondition = new BlockStoreQueueFlushCondition(this.chainState, this.initialBlockDownloadState.Object);

            this.blockStoreQueue = new BlockStoreQueue(longChainIndexer, this.chainState, blockStoreFlushCondition, this.storeSettings,
                this.blockRepositoryMock.Object, this.loggerFactory, new Mock<INodeStats>().Object, this.asyncProvider, new Mock<IInitialBlockDownloadState>().Object);

            this.blockStoreQueue.Initialize();
            this.chainState.ConsensusTip = longChainIndexer.Tip;

            // Send all the blocks to the block store except for the last one because that will trigger batch saving because of reaching the tip.
            for (int i = 1; i < count; i++)
            {
                ChainedHeader header = longChainIndexer.GetHeader(i);

                this.blockStoreQueue.AddToPending(new ChainedHeaderBlock(block, header));
            }

            await WaitUntilQueueIsEmptyAsync().ConfigureAwait(false);
            Assert.Equal(longChainIndexer.GetHeader(count - 1), this.chainState.BlockStoreTip);
            Assert.True(this.repositorySavesCount > 0);
        }

        [Fact]
        public async Task BatchIsSavedOnShutdownAsync()
        {
            this.repositoryTipHashAndHeight = new HashHeightPair(this.chainIndexer.Genesis.HashBlock, 0);

            var blockStoreFlushConditionMock = new Mock<IBlockStoreQueueFlushCondition>();
            blockStoreFlushConditionMock.Setup(s => s.ShouldFlush).Returns(false);
            this.blockStoreQueue = new BlockStoreQueue(this.chainIndexer, this.chainState, blockStoreFlushConditionMock.Object, this.storeSettings, this.blockRepositoryMock.Object, this.loggerFactory, new Mock<INodeStats>().Object, this.asyncProvider, new Mock<IInitialBlockDownloadState>().Object);

            this.blockStoreQueue.Initialize();

            ChainedHeader lastHeader = null;

            for (int i = 1; i < this.chainIndexer.Height - 1; i++)
            {
                lastHeader = this.chainIndexer.GetHeader(i);
                Block block = this.network.Consensus.ConsensusFactory.CreateBlock();
                block.GetSerializedSize();

                this.blockStoreQueue.AddToPending(new ChainedHeaderBlock(block, lastHeader));
            }

            await this.WaitUntilQueueIsEmptyAsync().ConfigureAwait(false);

            Assert.Equal(this.chainState.BlockStoreTip, this.chainIndexer.Genesis);
            Assert.Equal(0, this.repositorySavesCount);

            this.nodeLifetime.StopApplication();
            this.blockStoreQueue.Dispose();

            Assert.Equal(this.chainState.BlockStoreTip, lastHeader);
            Assert.Equal(1, this.repositorySavesCount);
        }

        [Fact]
        public async Task BatchIsSavedWhenAtConsensusTipAsync()
        {
            this.repositoryTipHashAndHeight = new HashHeightPair(this.chainIndexer.Genesis.HashBlock, 0);

            var blockStoreFlushConditionMock = new Mock<IBlockStoreQueueFlushCondition>();
            blockStoreFlushConditionMock.Setup(s => s.ShouldFlush).Returns(false);
            this.blockStoreQueue = new BlockStoreQueue(this.chainIndexer, this.chainState, blockStoreFlushConditionMock.Object, this.storeSettings,
                this.blockRepositoryMock.Object, this.loggerFactory, new Mock<INodeStats>().Object, this.asyncProvider, new Mock<IInitialBlockDownloadState>().Object);

            this.blockStoreQueue.Initialize();
            this.chainState.ConsensusTip = this.chainIndexer.Tip;

            for (int i = 1; i <= this.chainIndexer.Height; i++)
            {
                ChainedHeader lastHeader = this.chainIndexer.GetHeader(i);
                Block block = this.network.Consensus.ConsensusFactory.CreateBlock();
                block.GetSerializedSize();

                if (i == this.chainIndexer.Height)
                {
                    await this.WaitUntilQueueIsEmptyAsync().ConfigureAwait(false);
                    blockStoreFlushConditionMock.Setup(s => s.ShouldFlush).Returns(true);
                }

                this.blockStoreQueue.AddToPending(new ChainedHeaderBlock(block, lastHeader));
            }

            await this.WaitUntilQueueIsEmptyAsync().ConfigureAwait(false);

            // Wait for store tip to finish saving
            int counter = 0;
            if (this.chainState.BlockStoreTip != this.chainIndexer.Tip)
            {
                Assert.True(counter < 10);
                counter++;
                await Task.Delay(500);
            }

            Assert.Equal(this.chainState.BlockStoreTip, this.chainIndexer.Tip);
            Assert.Equal(1, this.repositorySavesCount);
        }

        [Fact]
        public async Task ReorgedBlocksAreNotSavedAsync()
        {
            this.repositoryTipHashAndHeight = new HashHeightPair(this.chainIndexer.Genesis.HashBlock, 0);

            var blockStoreFlushConditionMock = new Mock<IBlockStoreQueueFlushCondition>();
            blockStoreFlushConditionMock.Setup(s => s.ShouldFlush).Returns(false);
            this.blockStoreQueue = new BlockStoreQueue(this.chainIndexer, this.chainState, blockStoreFlushConditionMock.Object, this.storeSettings,
                this.blockRepositoryMock.Object, this.loggerFactory, new Mock<INodeStats>().Object, this.asyncProvider, new Mock<IInitialBlockDownloadState>().Object);

            this.blockStoreQueue.Initialize();

            int reorgedChainLenght = 3;
            int realChainLenght = 6;

            // First present a short chain.
            ChainIndexer alternativeChainIndexer = CreateChain(reorgedChainLenght);
            for (int i = 1; i < alternativeChainIndexer.Height; i++)
            {
                Block block = this.network.Consensus.ConsensusFactory.CreateBlock();
                block.GetSerializedSize();

                this.blockStoreQueue.AddToPending(new ChainedHeaderBlock(block, alternativeChainIndexer.GetHeader(i)));
            }

            // Present second chain which has more work and reorgs blocks from genesis.
            for (int i = 1; i < realChainLenght; i++)
            {
                Block block = this.network.Consensus.ConsensusFactory.CreateBlock();
                block.GetSerializedSize();

                this.blockStoreQueue.AddToPending(new ChainedHeaderBlock(block, this.chainIndexer.GetHeader(i)));
            }

            await this.WaitUntilQueueIsEmptyAsync().ConfigureAwait(false);

            Assert.Equal(this.chainState.BlockStoreTip, this.chainIndexer.Genesis);
            Assert.Equal(0, this.repositorySavesCount);

            // Dispose block store to trigger save.
            this.nodeLifetime.StopApplication();
            this.blockStoreQueue.Dispose();

            // Make sure that blocks only from 2nd chain were saved.
            Assert.Equal(this.chainIndexer.GetHeader(realChainLenght - 1), this.chainState.BlockStoreTip);
            Assert.Equal(1, this.repositorySavesCount);
            Assert.Equal(realChainLenght - 1, this.repositoryTotalBlocksSaved);
        }

        /// <summary>
        /// Tests reorgs inside the batch and inside the database at the same time.
        /// </summary>
        /// <returns>The asynchronous task.</returns>
        [Fact]
        [Trait("Unstable", "True")]
        public async Task ReorgedBlocksAreDeletedFromRepositoryIfReorgDetectedAsync()
        {
            this.chainIndexer = CreateChain(1000);
            this.repositoryTipHashAndHeight = new HashHeightPair(this.chainIndexer.Genesis.HashBlock, 0);

            var blockStoreFlushCondition = new Mock<IBlockStoreQueueFlushCondition>();
            blockStoreFlushCondition.Setup(s => s.ShouldFlush).Returns(false);

            this.blockStoreQueue = new BlockStoreQueue(this.chainIndexer, this.chainState, blockStoreFlushCondition.Object, this.storeSettings,
                this.blockRepositoryMock.Object, this.loggerFactory, new Mock<INodeStats>().Object, this.asyncProvider, new Mock<IInitialBlockDownloadState>().Object);

            this.blockStoreQueue.Initialize();
            this.chainState.ConsensusTip = this.chainIndexer.Tip;

            // Sending 500 blocks to the queue.
            for (int i = 1; i < 500; i++)
            {
                Block block = this.network.Consensus.ConsensusFactory.CreateBlock();
                block.GetSerializedSize();

                this.blockStoreQueue.AddToPending(new ChainedHeaderBlock(block, this.chainIndexer.GetHeader(i)));
            }

            // Create alternative chain with fork point at 450.
            ChainedHeader prevBlock = this.chainIndexer.GetHeader(450);
            var alternativeBlocks = new List<ChainedHeader>();
            for (int i = 0; i < 100; i++)
            {
                BlockHeader header = this.network.Consensus.ConsensusFactory.CreateBlockHeader();
                header.Nonce = RandomUtils.GetUInt32();
                header.HashPrevBlock = prevBlock.HashBlock;
                header.Bits = Target.Difficulty1;

                var chainedHeader = new ChainedHeader(header, header.GetHash(), prevBlock);
                alternativeBlocks.Add(chainedHeader);
                prevBlock = chainedHeader;
            }

            ChainedHeader savedHeader = this.chainIndexer.Tip;

            this.chainIndexer.SetTip(alternativeBlocks.Last());
            this.chainState.ConsensusTip = this.chainIndexer.Tip;

            // Present alternative chain and trigger save.
            foreach (ChainedHeader header in alternativeBlocks)
            {
                Block block = this.network.Consensus.ConsensusFactory.CreateBlock();
                block.GetSerializedSize();

                if (header == alternativeBlocks.Last())
                    blockStoreFlushCondition.Setup(s => s.ShouldFlush).Returns(true);

                this.blockStoreQueue.AddToPending(new ChainedHeaderBlock(block, header));
            }

            await this.WaitUntilQueueIsEmptyAsync().ConfigureAwait(false);

            blockStoreFlushCondition.Setup(s => s.ShouldFlush).Returns(false);

            // Make sure only longest chain is saved.
            Assert.Equal(this.chainIndexer.Tip.Height, this.repositoryTotalBlocksSaved);

            // Present a new longer chain that will reorg the repository.
            this.chainIndexer.SetTip(savedHeader);
            this.chainState.ConsensusTip = this.chainIndexer.Tip;

            for (int i = 451; i <= this.chainIndexer.Height; i++)
            {
                Block block = this.network.Consensus.ConsensusFactory.CreateBlock();
                block.GetSerializedSize();

                if (i == this.chainIndexer.Height)
                    blockStoreFlushCondition.Setup(s => s.ShouldFlush).Returns(true);

                this.blockStoreQueue.AddToPending(new ChainedHeaderBlock(block, this.chainIndexer.GetHeader(i)));
            }

            await this.WaitUntilQueueIsEmptyAsync().ConfigureAwait(false);

            // Make sure chain is saved.
            Assert.Equal(this.chainIndexer.Tip.Height + alternativeBlocks.Count, this.repositoryTotalBlocksSaved);
            Assert.Equal(alternativeBlocks.Count, this.repositoryTotalBlocksDeleted);

            // Dispose block store.
            this.nodeLifetime.StopApplication();
            this.blockStoreQueue.Dispose();
        }

        [Fact]
        public void ThrowIfConsensusIsInitializedBeforeBlockStore()
        {
            this.repositoryTipHashAndHeight = new HashHeightPair(this.chainIndexer.Genesis.HashBlock, 0);
            this.chainState.ConsensusTip = this.chainIndexer.Tip;

            Assert.Throws<BlockStoreException>(() =>
            {
                this.blockStoreQueue.Initialize();
            });
        }

        [Fact]
        public void RetrieveBlocksFromCache()
        {
            List<ChainedHeaderBlock> chainedHeaderBlocks = this.AddBlocksToBlockStoreQueue();

            // Try to get 10 random blocks.
            for (int i = 0; i < 10; i++)
            {
                int blockIndex = this.random.Next(0, chainedHeaderBlocks.Count);

                Block blockToFind = chainedHeaderBlocks[blockIndex].Block;

                Block foundBlock = this.blockStoreQueue.GetBlock(blockToFind.GetHash());
                Assert.Equal(foundBlock, blockToFind);
            }
        }

        [Fact]
        public void RetrieveTransactionByIdFromCacheReturnsNullWhenNotIndexed()
        {
            List<ChainedHeaderBlock> chainedHeaderBlocks = this.AddBlocksToBlockStoreQueue();

            // Try to get 10 random transactions.
            for (int i = 0; i < 10; i++)
            {
                int blockIndex = this.random.Next(0, chainedHeaderBlocks.Count);

                Transaction txToFind = chainedHeaderBlocks[blockIndex].Block.Transactions.First();

                Transaction foundTx = this.blockStoreQueue.GetTransactionById(txToFind.GetHash());
                Assert.Null(foundTx);
            }
        }

        [Fact]
        public void RetrieveBlockIdByTxIdFromCache()
        {
            List<ChainedHeaderBlock> chainedHeaderBlocks = this.AddBlocksToBlockStoreQueue();

            // Try to get 10 random block ids.
            for (int i = 0; i < 10; i++)
            {
                int blockIndex = this.random.Next(0, chainedHeaderBlocks.Count);

                Transaction txToFind = chainedHeaderBlocks[blockIndex].Block.Transactions.First();

                uint256 foundBlockHash = this.blockStoreQueue.GetBlockIdByTransactionId(txToFind.GetHash());
                Assert.Equal(chainedHeaderBlocks[blockIndex].Block.GetHash(), foundBlockHash);
            }
        }

        private List<ChainedHeaderBlock> AddBlocksToBlockStoreQueue(int blocksCount = 500)
        {
            var chainedHeaderBlocks = new List<ChainedHeaderBlock>(blocksCount);

            for (int i = 0; i < blocksCount; i++)
            {
                Block block = TransactionsHelper.CreateDummyBlockWithTransaction(this.network, this.chainIndexer.Tip);

                var header = new ChainedHeader(block.Header, block.GetHash(), this.chainIndexer.Tip);

                this.chainIndexer.SetTip(header);
                chainedHeaderBlocks.Add(new ChainedHeaderBlock(block, header));
            }

            foreach (ChainedHeaderBlock chainedHeaderBlock in chainedHeaderBlocks)
                this.blockStoreQueue.AddToPending(chainedHeaderBlock);

            return chainedHeaderBlocks;
        }
    }
}