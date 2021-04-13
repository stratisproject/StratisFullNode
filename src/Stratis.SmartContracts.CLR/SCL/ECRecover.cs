using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;
using Stratis.SmartContracts;
using Stratis.SCL.Base;

namespace Stratis.SCL.Crypto
{
    public static class ECRecover
    {
        /// <summary>
        /// Takes a message and signatures and recovers the list of addresses that produced the signatures.
        /// </summary>
        /// <param name="signatures">The list of signatures of the message.</param>
        /// <param name="message">The message that was signed.</param>
        /// <param name="addresses">If not <c>null</c> the addresses returned are intersected with these addresses.</param>
        /// <returns>The list of addresses that produced the signatures and constrained to the list provided in <paramref name="addresses"/> if not <c>null</c>.</returns>
        public static Address[] VerifySignatures(byte[] signatures, byte[] message, Address[] addresses = null)
        {
            if (message == null || signatures == null)
                return null;

            IEnumerable<KeyId> keyIds = Operations.DeserializeSignatures(signatures)
                .Select(s => PubKey.RecoverFromMessage(message, Encoders.Base64.EncodeData(s)).Hash);

            if (addresses != null)
                keyIds = keyIds.Intersect(addresses.Select(a => new KeyId(a.ToBytes())));

            return keyIds.Select(s => Operations.DeserializeAddress(s.ToBytes())).ToArray();
        }
    }
}
