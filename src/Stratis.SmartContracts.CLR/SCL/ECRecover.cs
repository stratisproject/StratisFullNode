using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;
using Stratis.SmartContracts;

namespace Stratis.SCL.Crypto
{
    public static class ECRecover
    {
        private static Address DeserializeAddress(byte[] address)
        {
            var pn = new uint[5];
            for (int i = 0, offs = 0; i < pn.Length; i++, offs += 4)
                pn[i] = BitConverter.ToUInt32(address, offs);
            return new Address(pn[0], pn[1], pn[2], pn[3], pn[4]);
        }

        private static byte[][] DeserializeSignatures(byte[] signatures)
        {
            const int signatureLength = 65;
            const int minHeaderByte = 27;
            const int maxHeaderByte = 34;
            int cnt = signatures.Length / signatureLength;

            if (signatures.Length != signatureLength * cnt)
                return null;

            var buffer = new byte[cnt][];
            for (int i = 0; i < cnt; i++)
            {
                Array.Copy(signatures, i * signatureLength, buffer[i], 0, signatureLength);
                if (buffer[i][0] < minHeaderByte || buffer[i][0] > maxHeaderByte)
                    return null;
            }

            return buffer;
        }

        /// <summary>
        /// Takes a message and signatures and returns the list of addresses that produced the signatures.
        /// </summary>
        /// <param name="signatures">The list of signatures of the message.</param>
        /// <param name="message">The message that was signed.</param>
        /// <param name="addresses">If not <c>null</c> the addresses returned are intersected with these addresses.</param>
        /// <returns>The list of addresses that produced the signatures and constrained to the list provided in <paramref name="addresses"/> if not <c>null</c>.</returns>
        public static Address[] VerifySignatures(byte[] signatures, byte[] message, Address[] addresses = null)
        {
            if (message == null || signatures == null)
                return null;

            IEnumerable<KeyId> keyIds = DeserializeSignatures(signatures)
                .Select(s => PubKey.RecoverFromMessage(message, Encoders.Base64.EncodeData(s)).Hash);

            if (addresses != null)
                keyIds = keyIds.Intersect(addresses.Select(a => new KeyId(a.ToBytes())));

            return keyIds.Select(s => DeserializeAddress(s.ToBytes())).ToArray();
        }
    }
}
