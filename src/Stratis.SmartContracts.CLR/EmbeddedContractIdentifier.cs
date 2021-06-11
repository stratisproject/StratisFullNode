using System;
using System.Linq;
using NBitcoin;

namespace Stratis.SmartContracts.CLR
{
    public enum EmbeddedContractType
    {
        Authentication = 1,
        Multisig = 2
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class EmbeddedContractAttribute : Attribute
    {
        public EmbeddedContractType ContractType { get; private set; }

        public EmbeddedContractAttribute(EmbeddedContractType contractType)
        {
            this.ContractType = contractType;
        }

        public static EmbeddedContractType GetEmbeddedContractTypeId(Type contractType)
        {
            return contractType.GetCustomAttributes(typeof(EmbeddedContractAttribute), true)
                .OfType<EmbeddedContractAttribute>().FirstOrDefault().ContractType;
        }
    }

    public struct EmbeddedContractIdentifier
    {
        private static byte[] embeddedContractSignature = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 };
        private uint160 value;

        public EmbeddedContractIdentifier(uint160 id)
        {
            this.value = id;
        }

        public EmbeddedContractIdentifier(Type contractType, uint version)
        {
            EmbeddedContractType contractTypeId = EmbeddedContractAttribute.GetEmbeddedContractTypeId(contractType);
            this.value = new uint160(BitConverter.GetBytes(version).Concat(BitConverter.GetBytes((ulong)contractTypeId)).Concat(embeddedContractSignature).ToArray());
        }

        public uint Version { get => BitConverter.ToUInt32(this.value.ToBytes()); }

        public static implicit operator uint160(EmbeddedContractIdentifier embeddedContractIdentifier)
        {
            return embeddedContractIdentifier.value;
        }

        public static bool IsEmbedded(uint160 id)
        {
            return BitConverter.ToUInt64(id.ToBytes(), 12) == BitConverter.ToUInt64(embeddedContractSignature);
        }
    }
}