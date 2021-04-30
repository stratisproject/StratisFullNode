using System.Linq;
using NBitcoin;

namespace Stratis.SmartContracts.CLR
{
    public struct EmbeddedCodeHash
    {
        private static byte[] embeddedHashSignature = new byte[12] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private uint256 value;

        public EmbeddedCodeHash(uint256 codeHash)
        {
            this.value = codeHash;
        }

        public EmbeddedCodeHash(uint160 contractIdentifier)
        {
            this.value = new uint256(embeddedHashSignature.Concat(contractIdentifier.ToBytes()).ToArray());
        }

        public EmbeddedContractIdentifier Id { get => new EmbeddedContractIdentifier(new uint160(ByteArrayExtensions.SafeSubarray(this.value.ToBytes(), 12, 20))); }

        public static implicit operator uint256(EmbeddedCodeHash embeddedCodeHash)
        {
            return embeddedCodeHash.value;
        }

        /// <summary>
        /// Determines whether the hash is an "embedded code hash".
        /// </summary>
        /// <param name="hash">Hash to evaluate.</param>
        /// <returns><c>True</c> if its an embedded code hash and <c>false</c> otherwise.</returns>
        public static bool IsEmbeddedCodeHash(uint256 hash)
        {
            return hash.GetLow64() == 0 && (hash >> 8).GetLow32() == 0 && EmbeddedContractIdentifier.IsEmbedded(new EmbeddedCodeHash(hash).Id);
        }
    }
}
