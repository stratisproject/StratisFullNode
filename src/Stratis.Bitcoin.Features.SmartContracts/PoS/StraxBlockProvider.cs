using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Features.Miner;
using Stratis.Bitcoin.Mining;

namespace Stratis.Bitcoin.Features.SmartContracts.PoS
{
    public class StraxBlockProvider : IBlockProvider
    {
        private readonly Network network;

        /// <summary>Defines how proof of stake blocks are built when smart contracts are activated.</summary>
        private readonly SmartContractPosBlockDefinition smartContractPosBlockDefinition;

        /// <summary>Defines how proof of stake blocks are built.</summary>
        private readonly PosBlockDefinition posBlockDefinition;

        /// <summary>Defines how proof of work blocks are built on a proof-of-stake network.</summary>
        private readonly PosPowBlockDefinition posPowBlockDefinition;

        private readonly ISmartContractActivationProvider smartContractPosActivationProvider;

        public StraxBlockProvider(Network network, IEnumerable<BlockDefinition> definitions, ISmartContractActivationProvider smartContractPosActivationProvider)
        {
            this.network = network;

            this.smartContractPosBlockDefinition = definitions.OfType<SmartContractPosBlockDefinition>().FirstOrDefault();
            this.posBlockDefinition = definitions.OfType<PosBlockDefinition>().FirstOrDefault();
            this.posPowBlockDefinition = definitions.OfType<PosPowBlockDefinition>().FirstOrDefault();
            this.smartContractPosActivationProvider = smartContractPosActivationProvider;
        }

        /// <inheritdoc/>
        public BlockTemplate BuildPosBlock(ChainedHeader chainTip, Script script)
        {
            if (this.smartContractPosActivationProvider.IsActive(chainTip))
                return this.smartContractPosBlockDefinition.Build(chainTip, script);

            return this.posBlockDefinition.Build(chainTip, script);
        }

        /// <inheritdoc/>
        public BlockTemplate BuildPowBlock(ChainedHeader chainTip, Script script)
        {
            return this.posPowBlockDefinition.Build(chainTip, script);
        }

        /// <inheritdoc/>
        public void BlockModified(ChainedHeader chainTip, Block block)
        {
            if (BlockStake.IsProofOfStake(block))
            {
                if (this.smartContractPosActivationProvider.IsActive(chainTip))
                {
                    this.smartContractPosBlockDefinition.BlockModified(chainTip, block);
                }
                else
                {
                    this.posBlockDefinition.BlockModified(chainTip, block);
                }
            }
            else
            {
                this.posPowBlockDefinition.BlockModified(chainTip, block);
            }
        }
    }
}
