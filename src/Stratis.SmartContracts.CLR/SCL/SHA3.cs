using HashLib;

namespace Stratis.SCL.Crypto
{
    public static class SHA3
    {
        /// <summary>
        /// Returns a 32-byte Keccak256 hash of the given bytes.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static byte[] Keccak256(byte[] input)
        {
            return HashFactory.Crypto.SHA3.CreateKeccak256().ComputeBytes(input).GetBytes();
        }
    }
}
