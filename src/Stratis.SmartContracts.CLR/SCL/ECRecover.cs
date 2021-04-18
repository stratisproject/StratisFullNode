using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR;

namespace Stratis.SCL.Crypto
{
    public static class ECRecover
    {
        /// <summary>
        /// Takes a message and signatures and recovers the list of addresses that produced the signatures.
        /// </summary>
        /// <param name="signatures">The list of signatures of the message.</param>
        /// <param name="message">The message that was signed.</param>
        /// <param name="addresses">The addresses returned are intersected with these addresses (if not <c>null</c>).</param>
        /// <returns>The list of addresses that produced the signatures and constrained to the list provided in <paramref name="addresses"/>.</returns>
        private static Address[] VerifySignatures(string[] signatures, byte[] message, Address[] addresses)
        {
            try
            { 
                if (message == null || signatures == null)
                    return null;

                IEnumerable<KeyId> keyIds = signatures.Select(s => PubKey.RecoverFromMessage(message, s).Hash);

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
        public static bool TryGetVerifiedSignatures(string[] signatures, string message, Address[] addresses, out Address[] verifiedAddresses)
        {
            if (addresses == null)
            {
                verifiedAddresses = null;
                return false;
            }

            verifiedAddresses = VerifySignatures(signatures, System.Text.Encoding.ASCII.GetBytes(message), addresses);

            return verifiedAddresses != null;
        }
    }
}
