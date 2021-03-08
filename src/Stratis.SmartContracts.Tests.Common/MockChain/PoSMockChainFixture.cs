using System;
using NBitcoin;
using Stratis.Bitcoin.Features.Consensus.Rules.CommonRules;
using Stratis.Bitcoin.Features.SmartContracts.PoA.MempoolRules;
using Stratis.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Stratis.Bitcoin.Networks;
using Stratis.SmartContracts.Networks;

namespace Stratis.SmartContracts.Tests.Common.MockChain
{

    public class PoSMockChainFixture : IMockChainFixture, IDisposable
    {
        private readonly SmartContractNodeBuilder builder;
        public IMockChain Chain { get; }

        public PoSMockChainFixture() : this(2) { }

        protected PoSMockChainFixture(int nodeNum)
        {
            var network = new StraxRegTest();
            network.Consensus.CoinbaseMaturity = 0;

            // TODO: The PoS tests seem to use the same network class to do sets of tests with different rule requirements (signed/unsigned). Need to normalise it to avoid this hack.
            network.Consensus.MempoolRules.Remove(typeof(AllowedCodeHashLogicMempoolRule));

            this.builder = SmartContractNodeBuilder.Create(this);

            CoreNode factory(int nodeIndex) => this.builder.CreateStraxNode(network, nodeIndex).Start();
            PoSMockChain mockChain = new PoSMockChain(nodeNum, factory).Build();
            this.Chain = mockChain;
            MockChainNode node1 = this.Chain.Nodes[0];
            MockChainNode node2 = this.Chain.Nodes[1];

            // Get premine
            mockChain.MineBlocks(10);

            // Send half to other from whoever received premine
            if ((long)node1.WalletSpendableBalance > (long)node2.WalletSpendableBalance)
            {
                this.PayHalfPremine(node1, node2);
            }
            else
            {
                this.PayHalfPremine(node2, node1);
            }
        }

        private void PayHalfPremine(MockChainNode from, MockChainNode to)
        {
            from.SendTransaction(to.MinerAddress.ScriptPubKey, new Money(from.CoreNode.FullNode.Network.Consensus.PremineReward.Satoshi / 2, MoneyUnit.Satoshi));
            from.WaitMempoolCount(1);
            this.Chain.MineBlocks(1);
        }

        public void Dispose()
        {
            this.builder.Dispose();
            this.Chain.Dispose();
        }
    }

    public class PoSMockChainFixture3Nodes : PoSMockChainFixture
    {
        public PoSMockChainFixture3Nodes() : base(3) { }
    }
}