using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NBitcoin;
using Nethereum.RLP;
using Stratis.Bitcoin.Consensus;
using Stratis.SmartContracts.CLR.Exceptions;
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
        private readonly IConsensusManager consensusManager;
        private readonly ContractPrimitiveSerializerV1 serializerV1;

        public ContractPrimitiveSerializer(Network network, IConsensusManager consensusManager)
            : base(network)
        {
            this.consensusManager = consensusManager;
            this.serializerV1 = new ContractPrimitiveSerializerV1(network);
        }

        public override object Deserialize(Type type, byte[] stream)
        {
            if (this.consensusManager.Tip.Height > 0) // TODO
                return this.serializerV1.Deserialize(type, stream);

            return base.Deserialize(type, stream);
        }
    }
}
