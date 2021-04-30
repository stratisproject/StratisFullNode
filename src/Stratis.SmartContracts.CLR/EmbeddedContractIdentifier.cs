using System;
using System.Linq;
using NBitcoin;

namespace Stratis.SmartContracts.CLR
{
    public struct EmbeddedContractIdentifier
    {
        private const string signature = "Embedded";
        private static byte[] embeddedContractSignature = System.Text.Encoding.ASCII.GetBytes(signature);
        private uint160 value;

        public EmbeddedContractIdentifier(uint160 id)
        {
            this.value = id;
        }

        public EmbeddedContractIdentifier(ulong contractTypeId, uint version)
        {
            this.value = new uint160(embeddedContractSignature.Concat(BitConverter.GetBytes(contractTypeId)).Concat(BitConverter.GetBytes(version)).ToArray());
        }

        public ulong ContractTypeId { get => BitConverter.ToUInt64(this.value.ToBytes(), 8); }

        public uint Version { get => BitConverter.ToUInt32(this.value.ToBytes(), 16); }

        public static implicit operator uint160(EmbeddedContractIdentifier systemContractIdentifier)
        {
            return systemContractIdentifier.value;
        }

        public static bool IsEmbedded(uint160 id)
        {
            return System.Text.Encoding.ASCII.GetString(id.ToBytes(), 0, embeddedContractSignature.Length) == signature;
        }
    }

}
