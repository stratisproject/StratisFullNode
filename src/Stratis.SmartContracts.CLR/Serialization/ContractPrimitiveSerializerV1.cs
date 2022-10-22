using System;
using NBitcoin;
using TracerAttributes;

namespace Stratis.SmartContracts.CLR.Serialization
{
    /// <summary>
    /// This class serializes and deserializes specific data types
    /// when persisting items inside a smart contract.
    /// </summary>
    [NoTrace]
    public class ContractPrimitiveSerializerV1 : ContractPrimitiveSerializerV2
    {
        public ContractPrimitiveSerializerV1(Network network)
            : base(network)
        {
        }

        public override object Deserialize(Type type, byte[] stream)
        {
            if (stream == null || stream.Length == 0)
                return null;

            return base.DeserializeBytes(type, stream);
        }
    }
}
