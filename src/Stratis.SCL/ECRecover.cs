using System;
using NBitcoin;
using Stratis.SmartContracts;

namespace Stratis.SCL.Crypto
{
    public static class ECRecover
    {
        /// <summary>
        /// Retrieves the address of the signer of an ECDSA signature.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="signature">The ECDSA signature prepended with header information specifying the correct value of recId.</param>
        /// <returns>The Address for the signer of a signature.</returns>
        public static Address GetSigner(byte[] message, byte[] signature)
        {
            uint256 hashedUint256 = GetUint256FromMessage(message);

            PubKey pubKey = PubKey.RecoverCompact(hashedUint256, signature);

            return CreateAddress(pubKey.Hash.ToBytes());
        }

        private static uint256 GetUint256FromMessage(byte[] message)
        {
            return new uint256(SHA3.Keccak256(message));
        }

        private static Address CreateAddress(byte[] bytes)
        {
            uint pn0 = BitConverter.ToUInt32(bytes, 0);
            uint pn1 = BitConverter.ToUInt32(bytes, 4);
            uint pn2 = BitConverter.ToUInt32(bytes, 8);
            uint pn3 = BitConverter.ToUInt32(bytes, 12);
            uint pn4 = BitConverter.ToUInt32(bytes, 16);

            return new Address(pn0, pn1, pn2, pn3, pn4);
        }
    }
}
