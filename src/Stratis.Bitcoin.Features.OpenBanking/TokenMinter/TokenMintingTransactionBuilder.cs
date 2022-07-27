using System.Text.Json;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using Stratis.Bitcoin.Features.OpenBanking.OpenBanking;
using Stratis.Bitcoin.Features.SmartContracts.MetadataTracker;
using Stratis.Bitcoin.Features.SmartContracts.Models;
using Stratis.Bitcoin.Features.SmartContracts.Wallet;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Serialization;

namespace Stratis.Bitcoin.Features.OpenBanking.TokenMinter
{
    public class TokenMintingTransactionBuilder : ITokenMintingTransactionBuilder
    {
        private const string mintingMethod = "MintWithMetadata";
        private readonly Network network;
        private readonly IMetadataTracker metadataTracker;
        private readonly OpenBankingSettings openBankingSettings;
        private readonly ISmartContractTransactionService smartContractTransactionService;
        private readonly ILogger logger;

        public TokenMintingTransactionBuilder(Network network, IMetadataTracker metadataTracker, OpenBankingSettings openBankingSettings, ISmartContractTransactionService smartContractTransactionService, ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            this.network = network;
            this.metadataTracker = metadataTracker;
            this.openBankingSettings = openBankingSettings;
            this.smartContractTransactionService = smartContractTransactionService;
        }

        private Address Base58ToAddress(BitcoinAddress bitcoinAddress)
        {
            return new uint160(PayToPubkeyHashTemplate.Instance.ExtractScriptPubKeyParameters(bitcoinAddress.ScriptPubKey).ToBytes()).ToAddress();
        }

        /// <inheritdoc/>
        public Transaction BuildSignedTransaction(IOpenBankAccount openBankAccount, OpenBankDeposit openBankDeposit)
        {
            MetadataTrackerDefinition metadataTrackingDefinition = this.metadataTracker.GetTracker(openBankAccount.MetaDataTable);

            var serializer = new MethodParameterStringSerializer(this.network);

            var request = new BuildCallContractTransactionRequest()
            {
                AccountName = this.openBankingSettings.WalletAccount,
                WalletName = this.openBankingSettings.WalletName,
                MethodName = mintingMethod,
                Amount = "0",
                FeeAmount = "0.04",
                Password = this.openBankingSettings.WalletPassword,
                GasPrice = 100,
                GasLimit = 250_000,
                Sender = this.openBankingSettings.WalletAddress,
                ContractAddress = metadataTrackingDefinition.Contract,
                Parameters = new string[] {
                    // The address that will receive the funds.
                    serializer.Serialize(Base58ToAddress(openBankDeposit.ParseAddressFromReference(this.network))),
                    // Amount
                    serializer.Serialize((UInt256)openBankDeposit.Amount.Satoshi), // TODO: Minus the fees.
                    // Metadata
                    serializer.Serialize(Encoders.ASCII.EncodeData(openBankDeposit.KeyBytes))
                }
            };

            BuildCallContractTransactionResponse response = this.smartContractTransactionService.BuildCallTx(request);

            if (!response.Success)
            {
                this.logger.LogError("Failed to create minting transaction for deposit '{0}'.", JsonSerializer.Serialize(openBankDeposit));
                return null;
            }

            Transaction transaction = this.network.CreateTransaction(response.Hex);

            this.logger.LogDebug("Created minting transaction '{0}' for deposit '{1}'.", transaction.ToHex(), JsonSerializer.Serialize(openBankDeposit));

            return transaction;
        }
    }
}
