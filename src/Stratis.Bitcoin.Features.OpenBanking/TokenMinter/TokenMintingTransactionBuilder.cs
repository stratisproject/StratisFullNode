using System;
using System.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;
using Stratis.Bitcoin.Features.OpenBanking.OpenBanking;
using Stratis.Bitcoin.Features.SmartContracts.MetadataTracker;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR;

namespace Stratis.Bitcoin.Features.OpenBanking.TokenMinter
{
    public class TokenMintingTransactionBuilder : ITokenMintingTransactionBuilder
    {
        private const string mintingMethod = "BurnWithMetadata"; // "MintWithMetadata"
        private readonly Network network;
        private readonly ICallDataSerializer callDataSerializer;
        private readonly IMetadataTracker metadataTracker;
        private readonly OpenBankingSettings openBankingSettings;

        public TokenMintingTransactionBuilder(Network network, ICallDataSerializer callDataSerializer, IMetadataTracker metadataTracker, OpenBankingSettings openBankingSettings)
        {
            this.network = network;
            this.callDataSerializer = callDataSerializer;
            this.metadataTracker = metadataTracker;
            this.openBankingSettings = openBankingSettings;
        }

        public Transaction BuildSignedTransaction(IOpenBankAccount openBankAccount, OpenBankDeposit openBankDeposit)
        {
            var gasPriceSatoshis = 20;
            var gasLimit = 4_000_000;
            var gasBudgetSatoshis = gasPriceSatoshis * gasLimit;
            var relayFeeSatoshis = 10000;
            var totalSuppliedSatoshis = gasBudgetSatoshis + relayFeeSatoshis;

            MetadataTrackerDefinition metadataTrackingDefinition = this.metadataTracker.GetTracker(openBankAccount.MetaDataTrackerEnum);

            uint160 contractAddress = metadataTrackingDefinition.Contract.ToUint160(this.network);
            uint160 receiver = openBankDeposit.Reference.ToUint160(this.network);
            // TODO: Ensure that sorting by ExternalId will result in chronological ordering.
            var methodParameters = new object[] { receiver.ToAddress(), (UInt256)openBankDeposit.Amount.Satoshi, Encoders.ASCII.EncodeData(openBankDeposit.KeyBytes) };
            var contractTxData = new ContractTxData(1, (ulong)gasPriceSatoshis, (Stratis.SmartContracts.RuntimeObserver.Gas)gasLimit, contractAddress, mintingMethod, methodParameters);
            var serialized = this.callDataSerializer.Serialize(contractTxData);

            Transaction funding = new Transaction
            {
                Outputs =
                {
                    new TxOut(totalSuppliedSatoshis, new Script())
                }
            };

            var transactionBuilder = new TransactionBuilder(this.network);
            //transactionBuilder.AddCoins(funding); // To cover gas price.
            transactionBuilder.SendFees(relayFeeSatoshis + gasBudgetSatoshis);
            transactionBuilder.Send(new Script(serialized), 0);

            return transactionBuilder.BuildTransaction(true);
        }
    }
}
