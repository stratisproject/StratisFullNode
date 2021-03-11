using System;
using System.Collections.Generic;
using System.Threading;
using NBitcoin;
using Stratis.Bitcoin.Features.Miner.Interfaces;
using Stratis.Bitcoin.Features.Miner.Staking;
using Stratis.Bitcoin.IntegrationTests.Common;
using Stratis.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Stratis.Bitcoin.Tests.Common;

namespace Stratis.SmartContracts.Tests.Common.MockChain
{
    /// <summary>
    /// Facade for NodeBuilder.
    /// </summary>
    /// <remarks>TODO: This and PoWMockChain could share most logic</remarks>
    public class PoSMockChain : IMockChain
    {
        private readonly Func<int, CoreNode> nodeFactory;
        private readonly Mnemonic initMnemonic;
        protected readonly MockChainNode[] nodes;
        public IReadOnlyList<MockChainNode> Nodes
        {
            get { return this.nodes; }
        }

        protected int chainHeight;

        public PoSMockChain(int numNodes, Func<int, CoreNode> nodeFactory, Mnemonic mnemonic = null)
        {
            this.nodes = new MockChainNode[numNodes];
            this.nodeFactory = nodeFactory;
            this.initMnemonic = mnemonic;
        }

        public PoSMockChain Build()
        {
            for (int nodeIndex = 0; nodeIndex < this.nodes.Length; nodeIndex++)
            {
                CoreNode node = this.nodeFactory(nodeIndex);

                for (int j = 0; j < nodeIndex; j++)
                {
                    MockChainNode otherNode = this.nodes[j];
                    TestHelper.Connect(node, otherNode.CoreNode);
                }

                this.nodes[nodeIndex] = new MockChainNode(node, this, this.initMnemonic);
            }

            return this;
        }

        /// <summary>
        /// Halts the main thread until all nodes on the network are synced.
        /// </summary>
        public void WaitForAllNodesToSync()
        {
            if (this.nodes.Length == 1)
            {
                TestBase.WaitLoop(() => TestHelper.IsNodeSynced(this.nodes[0].CoreNode));
                return;
            }

            for (int i = 0; i < this.nodes.Length - 1; i++)
            {
                TestBase.WaitLoop(() => TestHelper.AreNodesSynced(this.nodes[i].CoreNode, this.nodes[i + 1].CoreNode));
            }
        }

        public void WaitAllMempoolCount(int num)
        {
            for (int i = 0; i < this.Nodes.Count; i++)
            {
                this.Nodes[i].WaitMempoolCount(num);
            }
        }

        public void MineBlocks(int count)
        {
            CoreNode node1 = this.Nodes[0].CoreNode;
            ChainIndexer chainIndexer1 = node1.FullNode.NodeService<ChainIndexer>();
            int tipHeight = chainIndexer1.Tip.Height;
            if (tipHeight >= node1.FullNode.Network.Consensus.LastPOWBlock)
            {
                (node1.FullNode.NodeService<IPosMinting>() as TestStraxMinting).MineBlocks(count);
            }
            else
            {
                TestHelper.MineBlocks(node1, count);
            }

            this.WaitForAllNodesToSync();
        }

        public void Dispose()
        {
        }      
    }
}