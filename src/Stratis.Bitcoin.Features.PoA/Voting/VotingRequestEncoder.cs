using System;
using System.IO;
using DBreeze.Utils;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Features.PoA;

namespace Stratis.Bitcoin.PoA.Features.Voting
{
    public class VotingRequestEncoder
    {
        public static readonly byte[] VotingRequestOutputPrefixBytes = new byte[] { 143, 18, 13, 250 };

        public const int VotingRequestDataMaxSerializedSize = ushort.MaxValue;

        private readonly ILogger logger;

        public VotingRequestEncoder(ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        /// <summary>Encodes voting request data.</summary>
        public byte[] Encode(VotingRequest votingRequestData)
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

        /// <summary>Decodes the voting request.</summary>
        /// <exception cref="PoAConsensusErrors.VotingDataInvalidFormat">Thrown in case voting data format is invalid.</exception>
        public VotingRequest Decode(byte[] votingRequestDataBytes)
        {
            try
            {
                if (votingRequestDataBytes.Length > VotingRequestDataMaxSerializedSize)
                {
                    this.logger.LogTrace("(-)[INVALID_SIZE]");
                    PoAConsensusErrors.VotingRequestInvalidFormat.Throw();
                }

                using (var memoryStream = new MemoryStream(votingRequestDataBytes))
                {
                    var deserializeStream = new BitcoinStream(memoryStream, false);

                    byte[] prefix = new byte[VotingRequestOutputPrefixBytes.Length];
                    deserializeStream.ReadWrite(ref prefix);
                    if (!prefix._ByteArrayEquals(VotingRequestOutputPrefixBytes))
                        PoAConsensusErrors.VotingRequestInvalidFormat.Throw();

                    var decoded = new VotingRequest();

                    deserializeStream.ReadWrite(ref decoded);

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
