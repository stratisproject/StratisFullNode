using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;
using Stratis.SmartContracts;
using Stratis.SCL.Base;
using Stratis.SmartContracts.CLR;

namespace Stratis.SCL.Crypto
{
    public static class ECRecover
    {
        private static Address[] VerifySignatures(byte[] signatures, byte[] message, Address[] addresses)
        {
            const int signatureLength = 65;
            const int minHeaderByte = 27;
            const int maxHeaderByte = 34;

            try
            { 
                if (message == null || signatures == null)
                    return null;

                byte[][] sigArray = Operations.UnflattenArray(signatures, signatureLength);
                if (sigArray == null)
                    return null;

                if (sigArray.Any(s => s.Length == 0 || s[0] < minHeaderByte || s[0] > maxHeaderByte))
                    return null;

                IEnumerable<KeyId> keyIds = sigArray.Select(s => PubKey.RecoverFromMessage(message, Encoders.Base64.EncodeData(s)).Hash);

                if (addresses != null)
                    keyIds = keyIds.Intersect(addresses.Select(a => new KeyId(a.ToBytes())));

                return keyIds.Select(s => s.ToBytes().ToAddress()).ToArray();
            }
            catch (Exception)
            {
                return null;
            }

        }

        /// <summary>
        /// Takes a message and signatures and recovers the list of addresses that produced the signatures.
        /// </summary>
        /// <param name="signatures">The list of signatures of the message.</param>
        /// <param name="message">The message that was signed.</param>
        /// <param name="addresses">The addresses returned are intersected with these addresses.</param>
        /// <param name="verifiedAddresses">The list of addresses that produced the signatures and constrained to the list provided in <paramref name="addresses"/>.</param>
        /// <returns>The boolean value returned only indicates whether the operation could be performed. The number of verified addresses should still be checked.</returns>
        public static bool TryVerifySignatures(byte[] signatures, byte[] message, Address[] addresses, out Address[] verifiedAddresses)
        {
            if (addresses == null)
            {
                verifiedAddresses = null;
                return false;
            }

            verifiedAddresses = VerifySignatures(signatures, message, addresses);

            return verifiedAddresses != null;
        }
    }
}
