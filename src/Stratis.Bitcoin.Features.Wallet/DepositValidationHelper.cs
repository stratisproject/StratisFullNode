using System.Collections.Generic;
using System.Linq;
using System.Net;
using NBitcoin;
using NBitcoin.DataEncoders;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Networks;

namespace Stratis.Bitcoin.Features.Wallet
{
    public static class DepositValidationHelper
    {
        /// <summary> Conversion transaction deposits smaller than this threshold will be ignored. Denominated in STRAX.</summary>
        public static readonly Money ConversionTransactionMinimum = Money.Coins(250);

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

            IFederation federation = network.Federations?.GetOnlyFederation();
            if (federation == null)
                return false;

            var depositScript = PayToFederationTemplate.Instance.GenerateScriptPubKey(federation.Id).PaymentScript;

            depositsToMultisig = transaction.Outputs.Where(output =>
                output.ScriptPubKey == depositScript &&
                output.Value >= crossChainTransferMinimum).ToList();

            return depositsToMultisig.Any();
        }

        public static bool TryGetTarget(Transaction transaction, IOpReturnDataReader opReturnDataReader, out bool conversion, out string targetAddress, out int targetChain)
        {
            conversion = false;
            targetChain = 0 /* DestinationChain.STRAX */;

            // First check cross chain transfers from the STRAX to Cirrus network or vice versa.
            if (!opReturnDataReader.TryGetTargetAddress(transaction, out targetAddress))
            {
                // Else try and validate the destination adress by the destination chain.
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

        /// <summary>
        /// Determines if this is a cross-chain transfer and then validates the target address as required.
        /// </summary>
        /// <param name="network">The source network.</param>
        /// <param name="transaction">The transaction to validate.</param>
        /// <returns><c>True</c> if its a cross-chain transfer and <c>false</c> otherwise.</returns>
        /// <exception cref="FeatureException">If the address is invalid or inappropriate for the target network.</exception>
        public static bool ValidateCrossChainDeposit(Network network, Transaction transaction)
        {
            if (!TryGetDepositsToMultisig(network, transaction, Money.Zero, out List<TxOut> depositsToMultisig))
                return false;

            if (depositsToMultisig.Any(d => d.Value < Money.COIN))
            {
                throw new FeatureException(HttpStatusCode.BadRequest, "Amount below minimum.",
                    $"The cross-chain transfer amount is less than the minimum of 1.");
            }

            Network targetNetwork = null;

            if (network.Name.StartsWith("Cirrus"))
            {
                targetNetwork = StraxNetwork.MainChainNetworks[network.NetworkType]();
            }
            else if (network.Name.StartsWith("Strax"))
            {
                targetNetwork = new CirrusAddressValidationNetwork(network.Name.Replace("Strax", "Cirrus"));
            }
            else
            {
                return true;
            }

            IOpReturnDataReader opReturnDataReader = new OpReturnDataReader(targetNetwork);
            if (!TryGetTarget(transaction, opReturnDataReader, out _, out _, out _))
            {
                throw new FeatureException(HttpStatusCode.BadRequest, "No valid target address.",
                    $"The cross-chain transfer transaction contains no valid target address for the target network.");
            }

            return true;
        }
    }


    /// <summary>
    /// When running on Strax its difficult to get the correct Cirrus network class due to circular references.
    /// This is a bare-minimum network class for the sole purpose of address validation.
    /// </summary>
    public class CirrusAddressValidationNetwork : Network
    {
        public CirrusAddressValidationNetwork(string name) : base()
        {
            this.Name = name;
            this.Base58Prefixes = new byte[12][];
            switch (name)
            {
                case "CirrusMain":
                    this.Base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { 28 }; // C
                    this.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { 88 }; // c
                    break;
                case "CirrusTest":
                    this.Base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { 127 }; // t
                    this.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { 137 }; // x
                    break;
                case "CirrusRegTest":
                    this.Base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { 55 }; // P
                    this.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { 117 }; // p
                    break;
            }

            this.Base58Prefixes[(int)Base58Type.SECRET_KEY] = new byte[] { (239) };
            this.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_NO_EC] = new byte[] { 0x01, 0x42 };
            this.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_EC] = new byte[] { 0x01, 0x43 };
            this.Base58Prefixes[(int)Base58Type.EXT_PUBLIC_KEY] = new byte[] { (0x04), (0x35), (0x87), (0xCF) };
            this.Base58Prefixes[(int)Base58Type.EXT_SECRET_KEY] = new byte[] { (0x04), (0x35), (0x83), (0x94) };
            this.Base58Prefixes[(int)Base58Type.PASSPHRASE_CODE] = new byte[] { 0x2C, 0xE9, 0xB3, 0xE1, 0xFF, 0x39, 0xE2 };
            this.Base58Prefixes[(int)Base58Type.CONFIRMATION_CODE] = new byte[] { 0x64, 0x3B, 0xF6, 0xA8, 0x9A };
            this.Base58Prefixes[(int)Base58Type.STEALTH_ADDRESS] = new byte[] { 0x2b };
            this.Base58Prefixes[(int)Base58Type.ASSET_ID] = new byte[] { 115 };
            this.Base58Prefixes[(int)Base58Type.COLORED_ADDRESS] = new byte[] { 0x13 };

            Bech32Encoder encoder = Encoders.Bech32("tb");
            this.Bech32Encoders = new Bech32Encoder[2];
            this.Bech32Encoders[(int)Bech32Type.WITNESS_PUBKEY_ADDRESS] = encoder;
            this.Bech32Encoders[(int)Bech32Type.WITNESS_SCRIPT_ADDRESS] = encoder;
        }
    }
}
