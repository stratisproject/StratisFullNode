using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;
using NBitcoin.DataEncoders;
using Stratis.Bitcoin.Features.SmartContracts.PoA;
using Stratis.Sidechains.Networks;

namespace FederationSetup
{
    public class GenesisMiner
    {
        public string MineGenesisBlocks(SmartContractPoAConsensusFactory consensusFactory, string coinbaseText)
        {
            var output = new StringBuilder();

            Console.WriteLine("Looking for genesis blocks for the 3 networks, this might take a while.");
            Console.WriteLine(Environment.NewLine);

            var targets = new Dictionary<uint256, string>
            {
                { new Target(CirrusNetwork.NetworksSelector.Mainnet().GenesisBits).ToUInt256(), "-- MainNet network --" },
                { new Target(CirrusNetwork.NetworksSelector.Testnet().GenesisBits).ToUInt256(), "-- TestNet network --" },
                { new Target(CirrusNetwork.NetworksSelector.Regtest().GenesisBits).ToUInt256(), "-- RegTest network --" },
            };

            foreach (KeyValuePair<uint256, string> target in targets)
            {
                Block genesisBlock = this.GeneterateBlock(consensusFactory, coinbaseText, target.Key);
                output.AppendLine(this.NetworkOutput(genesisBlock, target.Value, coinbaseText));
            }

            return output.ToString();
        }

        private Block GeneterateBlock(SmartContractPoAConsensusFactory consensusFactory, string coinbaseText, uint256 target)
        {
            return MineGenesisBlock(consensusFactory, coinbaseText, new Target(target), Money.Zero);
        }

        private string NetworkOutput(Block genesisBlock, string network, string coinbaseText)
        {
            var header = (SmartContractPoABlockHeader)genesisBlock.Header;

            var output = new StringBuilder();
            output.AppendLine(network);
            output.AppendLine("bits: " + header.Bits);
            output.AppendLine("nonce: " + header.Nonce);
            output.AppendLine("time: " + header.Time);
            output.AppendLine("version: " + header.Version);
            output.AppendLine("hash: " + genesisBlock.GetHash());
            output.AppendLine("merkleroot: " + header.HashMerkleRoot);
            output.AppendLine("coinbase text: " + coinbaseText);
            output.AppendLine("hash state root: " + header.HashStateRoot);
            output.AppendLine(Environment.NewLine);

            return output.ToString();
        }

        public static Block MineGenesisBlock(SmartContractPoAConsensusFactory consensusFactory, string coinbaseText, Target target, Money genesisReward, int version = 1)
        {
            if (consensusFactory == null)
                throw new ArgumentException($"Parameter '{nameof(consensusFactory)}' cannot be null. Use 'new ConsensusFactory()' for Bitcoin-like proof-of-work blockchains and 'new PosConsensusFactory()' for Stratis-like proof-of-stake blockchains.");

            if (string.IsNullOrEmpty(coinbaseText))
                throw new ArgumentException($"Parameter '{nameof(coinbaseText)}' cannot be null. Use a news headline or any other appropriate string.");

            if (target == null)
                throw new ArgumentException($"Parameter '{nameof(target)}' cannot be null. Example use: new Target(new uint256(\"0000ffff00000000000000000000000000000000000000000000000000000000\"))");

            if (genesisReward == null)
                throw new ArgumentException($"Parameter '{nameof(genesisReward)}' cannot be null. Example use: 'Money.Coins(50m)'.");

            DateTimeOffset time = DateTimeOffset.Now;

            Transaction txNew = consensusFactory.CreateTransaction();
            txNew.Version = (uint)version;
            txNew.AddInput(new TxIn()
            {
                ScriptSig = new Script(
                    Op.GetPushOp(0),
                    new Op()
                    {
                        Code = (OpcodeType)0x1,
                        PushData = new[] { (byte)42 }
                    },
                    Op.GetPushOp(Encoders.ASCII.DecodeData(coinbaseText)))
            });

            txNew.AddOutput(new TxOut()
            {
                Value = genesisReward,
            });

            Block genesis = consensusFactory.CreateBlock();
            genesis.Header.BlockTime = time;
            genesis.Header.Bits = target;
            genesis.Header.Nonce = 0;
            genesis.Header.Version = version;
            genesis.Transactions.Add(txNew);
            genesis.Header.HashPrevBlock = uint256.Zero;
            genesis.UpdateMerkleRoot();

            ((SmartContractPoABlockHeader)genesis.Header).HashStateRoot = SmartContractPoABlockDefinition.StateRootEmptyTrie;

            // Iterate over the nonce until the proof-of-work is valid.
            // This will mean the block header hash is under the target.
            while (!genesis.CheckProofOfWork())
            {
                genesis.Header.Nonce++;
                if (genesis.Header.Nonce == 0)
                    genesis.Header.Time++;
            }

            return genesis;
        }
    }
}