using System.Collections.Generic;
using System.Threading;
using NBitcoin;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Primitives;

namespace Stratis.Bitcoin.Tests.Common
{
    public static class ChainedHeadersHelper
    {
        /// <summary>
        /// Nonce that will be used in header creation.
        /// Every header should have a different nonce to avoid possibility of creating headers with same hash.
        /// </summary>
        private static int currentNonce = 0;

        /// <summary>Creates specified number of consecutive headers.</summary>
        /// <param name="count">Number of blocks to generate.</param>
        /// <param name="prevBlock">If not <c>null</c> the headers will be generated on top of it.</param>
        /// <param name="includePrevBlock">If <c>true</c> <paramref name="prevBlock"/> will be added as a first item in the list or genesis header if <paramref name="prevBlock"/> is <c>null</c>.</param>
        public static List<ChainedHeader> CreateConsecutiveHeaders(int count, ChainedHeader prevBlock = null, bool includePrevBlock = false, Target bits = null, Network network = null, ChainIndexer chainIndexer = null)
        {
            var chainedHeaders = new List<ChainedHeader>();
            network = network ?? KnownNetworks.StraxMain;

            ChainedHeader tip = prevBlock;

            if (tip == null)
            {
                ChainedHeader genesis = CreateGenesisChainedHeader(network);
                tip = genesis;
            }

            if (includePrevBlock)
                chainedHeaders.Add(tip);

            uint256 hashPrevBlock = tip.HashBlock;

            uint time = 0;

            for (int i = 0; i < count; i++)
            {
                BlockHeader header = network.Consensus.ConsensusFactory.CreateBlockHeader();
                header.Time = time += (uint)network.Consensus.TargetSpacing.TotalSeconds;
                header.Nonce = (uint)Interlocked.Increment(ref currentNonce);
                header.HashPrevBlock = hashPrevBlock;
                header.Bits = bits ?? Target.Difficulty1;

                var chainedHeader = new ChainedHeader(header, header.GetHash(), tip);

                hashPrevBlock = chainedHeader.HashBlock;
                tip = chainedHeader;

                if (chainIndexer != null)
                    chainIndexer.SetTip(tip);

                chainedHeaders.Add(chainedHeader);
            }

            return chainedHeaders;
        }

        public static List<ChainedHeaderBlock> CreateConsecutiveHeadersAndBlocks(int count, bool includePrevBlock, Network network = null, ChainIndexer chainIndexer = null, bool withCoinbaseAndCoinStake = false, bool createCirrusReward = false)
        {
            List<ChainedHeader> chainedHeaders = CreateConsecutiveHeaders(count, null, includePrevBlock, network: network, chainIndexer: chainIndexer);

            foreach (ChainedHeader chainedHeader in chainedHeaders)
            {
                var block = new Block(chainedHeader.Header);

                if (withCoinbaseAndCoinStake)
                {
                    block.AddTransaction(TransactionsHelper.CreateCoinbase(network, chainedHeader.Height));

                    Transaction coinstakeTransaction = TransactionsHelper.CreateCoinStakeTransaction(network, new Key(), chainedHeader.Height, new uint256(0));
                    block.AddTransaction(coinstakeTransaction);

                    if (createCirrusReward)
                        TransactionsHelper.CreateCirrusRewardOutput(coinstakeTransaction, network);
                }

                chainedHeader.Block = block;
            }

            var blocks = new List<ChainedHeaderBlock>(chainedHeaders.Count);

            foreach (ChainedHeader chainedHeader in chainedHeaders)
            {
                blocks.Add(new ChainedHeaderBlock(chainedHeader.Block, chainedHeader));
            }

            return blocks;
        }

        /// <summary>Creates genesis header for stratis mainnet.</summary>
        public static ChainedHeader CreateGenesisChainedHeader(Network network = null)
        {
            if (network != null)
                return new ChainedHeader(network.GetGenesis().Header, network.GenesisHash, 0);

            return new ChainedHeader(new StraxMain().GetGenesis().Header, new StraxMain().GenesisHash, 0);
        }
    }
}
