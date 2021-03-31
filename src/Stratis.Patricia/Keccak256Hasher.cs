using HashLib;

namespace Stratis.Patricia
{
    public class Keccak256Hasher : IHasher
    {
        /// <inheritdoc />
        public byte[] Hash(byte[] input)
        {
            return HashFactory.Crypto.SHA3.CreateKeccak256().ComputeBytes(input).GetBytes();
        }
    }
}
