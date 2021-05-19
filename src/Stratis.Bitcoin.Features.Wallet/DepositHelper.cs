using System.Collections.Generic;
using System.Linq;
using NBitcoin;

namespace Stratis.Bitcoin.Features.Wallet
{
    public static class DepositHelper
    {
        /// <summary>
        /// This deposit extractor implementation only looks for a very specific deposit format.
        /// Deposits will have 2 outputs when there is no change.
        /// </summary>
        private const int ExpectedNumberOfOutputsNoChange = 2;

        /// <summary> Deposits will have 3 outputs when there is change.</summary>
        private const int ExpectedNumberOfOutputsChange = 3;

        public static bool TryGetDepositsToMultisig(Network network, Transaction transaction, Money crossChainTransferMinimum, out List<TxOut> depositsToMultisig)
        {
            depositsToMultisig = null;

            // Coinbase transactions can't have deposits.
            if (transaction.IsCoinBase)
                return false;

            // Deposits have a certain structure.
            if (transaction.Outputs.Count != ExpectedNumberOfOutputsNoChange && transaction.Outputs.Count != ExpectedNumberOfOutputsChange)
                return false;

            var depositScript = PayToFederationTemplate.Instance.GenerateScriptPubKey(network.Federations.GetOnlyFederation().Id).PaymentScript;

            depositsToMultisig = transaction.Outputs.Where(output =>
                output.ScriptPubKey == depositScript &&
                output.Value >= crossChainTransferMinimum).ToList();
            
            return depositsToMultisig.Any();
        }

        public static bool TryGetTarget(Transaction transaction, IOpReturnDataReader opReturnDataReader, out bool conversion, out string targetAddress, out int targetChain)
        {
            conversion = false;
            targetChain = 0 /* DestinationChain.STRAX */;

            // Check the common case first.
            if (!opReturnDataReader.TryGetTargetAddress(transaction, out targetAddress))
            {
                byte[] opReturnBytes = OpReturnDataReader.SelectBytesContentFromOpReturn(transaction).FirstOrDefault();

                if (opReturnBytes != null && InterFluxOpReturnEncoder.TryDecode(opReturnBytes, out int destinationChain, out targetAddress))
                {
                    targetChain = destinationChain;
                }
                else
                    return false;

                conversion = true;                
            }

            return true;
        }
    }
}
