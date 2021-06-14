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

    public static class EmbeddedContractAddress
    {
        const int zeroPadding = 12;

        public static uint160 Create(Type contractType, uint version)
        {
            // Stick to uint160 convention of big-endian. This results in the contract type id, followed by version in big-endian notation.
            return new uint160(BitConverter.GetBytes(version).Concat(BitConverter.GetBytes((uint)EmbeddedContractAttribute.GetEmbeddedContractTypeId(contractType))).Reverse().Concat(new byte[zeroPadding]).ToArray());
        }

        public static uint GetEmbeddedVersion(this uint160 address)
        {
            return BitConverter.ToUInt32(address.ToBytes().Reverse().ToArray(), zeroPadding /* Skip signature */);        
        }

        public static bool IsEmbedded(this uint160 address)
        {
            return BitConverter.ToUInt64(address.ToBytes(), sizeof(uint) + sizeof (uint)) == 0 && BitConverter.ToUInt32(address.ToBytes(), sizeof(uint) + sizeof(uint) + sizeof(ulong)) == 0;
        }
    }
}