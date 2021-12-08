using System;
using NBitcoin;
using Stratis.Bitcoin.Features.PoA;
using TracerAttributes;

namespace Stratis.SmartContracts.CLR.Serialization
{
    /// <summary>
    /// This class serializes and deserializes specific data types
    /// when persisting items inside a smart contract.
    /// </summary>
    [NoTrace]
    public class ContractPrimitiveSerializer : ContractPrimitiveSerializerV2
    {
        private readonly Network network;
        private readonly ChainIndexer chainIndexer;
        private readonly ContractPrimitiveSerializerV1 serializerV1;

        public ContractPrimitiveSerializer(Network network, ChainIndexer chainIndexer)
            : base(network)
        {
            this.network = network;
            this.chainIndexer = chainIndexer;
            this.serializerV1 = new ContractPrimitiveSerializerV1(network);
        }

        public override object Deserialize(Type type, byte[] stream)
        {
            if (this.network.Consensus.Options is PoAConsensusOptions poaConsensusOptions && this.chainIndexer.Tip.Height <= poaConsensusOptions.ContractSerializerV2ActivationHeight)
                return this.serializerV1.Deserialize(type, stream);

            return base.Deserialize(type, stream);
        }
    }
}
