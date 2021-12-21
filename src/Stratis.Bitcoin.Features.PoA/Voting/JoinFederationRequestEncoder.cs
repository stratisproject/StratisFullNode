using System;
using System.IO;
using DBreeze.Utils;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Features.PoA;

namespace Stratis.Bitcoin.PoA.Features.Voting
{
    public class JoinFederationRequestEncoder
    {
        public static readonly byte[] VotingRequestOutputPrefixBytes = new byte[] { 143, 18, 13, 250 };

        private readonly ILogger logger;

        public JoinFederationRequestEncoder()
        {
            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <summary>Encodes voting request data as an array of bytes.</summary>
        /// <param name="votingRequestData">See <see cref="JoinFederationRequest"/>.</param>
        /// <returns>An array of bytes encoding the voting request.</returns>
        public byte[] Encode(JoinFederationRequest votingRequestData)
        {
            using (var memoryStream = new MemoryStream())
            {
                var serializeStream = new BitcoinStream(memoryStream, true);
                var prefix = (byte[])VotingRequestOutputPrefixBytes.Clone();
                serializeStream.ReadWrite(ref prefix);
                serializeStream.ReadWrite(ref votingRequestData);

                return memoryStream.ToArray();
            }
        }

        /// <summary>Decodes a voting request that had previously been encoded as an array of bytes.</summary>
        /// <exception cref="PoAConsensusErrors.VotingDataInvalidFormat">Thrown in case voting data format is invalid.</exception>
        /// <param name="votingRequestDataBytes">An array of bytes encoding the voting request.</param>
        /// <returns>The <see cref="JoinFederationRequest"/>.</returns>
        public JoinFederationRequest Decode(byte[] votingRequestDataBytes)
        {
            try
            {
                using (var memoryStream = new MemoryStream(votingRequestDataBytes))
                {
                    var deserializeStream = new BitcoinStream(memoryStream, false);

                    byte[] prefix = new byte[VotingRequestOutputPrefixBytes.Length];
                    deserializeStream.ReadWrite(ref prefix);

                    // It's not a voting request if the prefix does not match.
                    if (!prefix._ByteArrayEquals(VotingRequestOutputPrefixBytes))
                        return null;

                    var decoded = new JoinFederationRequest();

                    deserializeStream.ReadWrite(ref decoded);

                    if (deserializeStream.ProcessedBytes != votingRequestDataBytes.Length)
                    {
                        this.logger.LogTrace("(-)[INVALID_SIZE]");
                        PoAConsensusErrors.VotingRequestInvalidFormat.Throw();
                    }

                    return decoded;
                }
            }
            catch (Exception e)
            {
                this.logger.LogDebug("Exception during deserialization: '{0}'.", e.ToString());
                this.logger.LogTrace("(-)[DESERIALIZING_EXCEPTION]");

                PoAConsensusErrors.VotingRequestInvalidFormat.Throw();
                return null;
            }
        }
    }
}
