using NBitcoin;
using Stratis.Bitcoin.Features.OpenBanking.OpenBanking;
using Stratis.Bitcoin.Features.SmartContracts.MetadataTracker;
using Stratis.Bitcoin.Features.SmartContracts.Models;
using Stratis.Bitcoin.Features.SmartContracts.Wallet;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Serialization;

namespace Stratis.Bitcoin.Features.OpenBanking.TokenMinter
{
    public class TokenMintingTransactionBuilder : ITokenMintingTransactionBuilder
    {
        private const string mintingMethod = "BurnWithMetadata"; // "MintWithMetadata"
        private readonly Network network;
        private readonly IWalletTransactionHandler walletTransactionHandler;
        private readonly IMetadataTracker metadataTracker;
        private readonly ICallDataSerializer callDataSerializer;
        private readonly OpenBankingSettings openBankingSettings;
        private readonly ISmartContractTransactionService smartContractTransactionService;

        public TokenMintingTransactionBuilder(Network network, IWalletTransactionHandler walletTransactionHandler, ICallDataSerializer callDataSerializer, IMetadataTracker metadataTracker, OpenBankingSettings openBankingSettings, ISmartContractTransactionService smartContractTransactionService)
        {
            this.network = network;
            this.walletTransactionHandler = walletTransactionHandler;
            this.metadataTracker = metadataTracker;
            this.callDataSerializer = callDataSerializer;
            this.openBankingSettings = openBankingSettings;
            this.smartContractTransactionService = smartContractTransactionService;
        }

        private Address Base58ToAddress(BitcoinAddress bitcoinAddress)
        {
            return new uint160(PayToPubkeyHashTemplate.Instance.ExtractScriptPubKeyParameters(bitcoinAddress.ScriptPubKey).ToBytes()).ToAddress();
        }

        public Transaction BuildSignedTransaction(IOpenBankAccount openBankAccount, OpenBankDeposit openBankDeposit)
        {
            MetadataTrackerDefinition metadataTrackingDefinition = this.metadataTracker.GetTracker(openBankAccount.MetaDataTrackerEnum);

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
                    // MethodName
                    serializer.Serialize(mintingMethod),
                    // Amount
                    serializer.Serialize((UInt256)openBankDeposit.Amount.Satoshi)
                }
            };

            BuildCallContractTransactionResponse response = this.smartContractTransactionService.BuildCallTx(request);

            if (!response.Success)
            {
                return null;
            }

            Transaction transaction = this.network.CreateTransaction(response.Hex);

            return transaction;
        }
    }
}
