using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NLog;

namespace Stratis.Features.PoA.Collateral
{
    public sealed class CollateralHeightCommitmentEncoder
    {
        /// <summary>Prefix used to identify OP_RETURN output with mainchain consensus height commitment.</summary>
        public static readonly byte[] HeightCommitmentOutputPrefixBytes = { 121, 13, 6, 253 };

        private readonly ILogger logger;

        public CollateralHeightCommitmentEncoder()
        {
            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <summary>Converts <paramref name="height"/> to a byte array which has a prefix of <see cref="HeightCommitmentOutputPrefixBytes"/>.</summary>
        /// <param name="height">That height at which the block was mined.</param>
        /// <returns>The encoded height in bytes.</returns>
        public byte[] EncodeCommitmentHeight(int height)
        {
            var bytes = new List<byte>(HeightCommitmentOutputPrefixBytes);

            bytes.AddRange(BitConverter.GetBytes(height));

            return bytes.ToArray();
        }

        /// <summary>Extracts the height commitment data from a transaction's coinbase <see cref="TxOut"/>.</summary>
        /// <param name="coinbaseTx">The transaction that should contain the height commitment data.</param>
        /// <returns>The commitment height, <c>null</c> if not found.</returns>
        public (int? height, uint? magic) DecodeCommitmentHeight(Transaction coinbaseTx)
        {
            IEnumerable<Script> opReturnOutputs = coinbaseTx.Outputs.Where(x => (x.ScriptPubKey.Length > 0) && (x.ScriptPubKey.ToBytes(true)[0] == (byte)OpcodeType.OP_RETURN)).Select(x => x.ScriptPubKey);

            byte[] commitmentData = null;
            byte[] magic = null;

            this.logger.Debug("Transaction contains {0} OP_RETURN outputs.", opReturnOutputs.Count());

            foreach (Script script in opReturnOutputs)
            {
                Op[] ops = script.ToOps().ToArray();

                if (ops.Length != 2 && ops.Length != 3)
                    continue;

                byte[] data = ops[1].PushData;

                bool correctPrefix = data.Take(HeightCommitmentOutputPrefixBytes.Length).SequenceEqual(HeightCommitmentOutputPrefixBytes);

                if (!correctPrefix)
                {
                    this.logger.Debug("Push data contains incorrect prefix for height commitment.");
                    continue;
                }

                commitmentData = data.Skip(HeightCommitmentOutputPrefixBytes.Length).ToArray();

                if (ops.Length == 3)
                    magic = ops[2].PushData;

                break;
            }

            if (commitmentData != null)
                return (BitConverter.ToInt32(commitmentData), ((magic == null) ? (uint?)null : BitConverter.ToUInt32(magic)));

            return (null, null);
        }
    }
}