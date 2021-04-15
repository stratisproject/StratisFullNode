using System;
using System.Collections.Generic;
using System.Linq;
using CSharpFunctionalExtensions;
using NBitcoin;
using NBitcoin.DataEncoders;
using Nethereum.RLP;
using Stratis.Bitcoin.Features.SmartContracts.Models;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.Features.Wallet.Services;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Serialization;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Bitcoin.Features.SmartContracts.Wallet
{
    /// <summary>
    /// Shared functionality for building SC transactions.
    /// </summary>
    public class SmartContractTransactionService : ISmartContractTransactionService
    {
        private const int MinConfirmationsAllChecks = 0;

        private const string SenderNoBalanceError = "The 'Sender' address you're trying to spend from doesn't have a balance available to spend. Please check the address and try again.";
        public const string TransferFundsToContractError = "Can't transfer funds to contract.";
        public const string SenderNotInWalletError = "Address not found in wallet.";
        public const string AccountNotInWalletError = "No account.";
        public const string InsufficientBalanceError = "Insufficient balance.";
        public const string InvalidOutpointsError = "Invalid outpoints.";

        private readonly Network network;
        private readonly IWalletManager walletManager;
        private readonly IWalletTransactionHandler walletTransactionHandler;
        private readonly IMethodParameterStringSerializer methodParameterStringSerializer;
        private readonly ICallDataSerializer callDataSerializer;
        private readonly IAddressGenerator addressGenerator;
        private readonly IStateRepositoryRoot stateRoot;
        private readonly IReserveUtxoService reserveUtxoService;

        public SmartContractTransactionService(
            Network network,
            IWalletManager walletManager,
            IWalletTransactionHandler walletTransactionHandler,
            IMethodParameterStringSerializer methodParameterStringSerializer,
            ICallDataSerializer callDataSerializer,
            IAddressGenerator addressGenerator,
            IStateRepositoryRoot stateRoot,
            IReserveUtxoService reserveUtxoService
            )
        {
            this.network = network;
            this.walletManager = walletManager;
            this.walletTransactionHandler = walletTransactionHandler;
            this.methodParameterStringSerializer = methodParameterStringSerializer;
            this.callDataSerializer = callDataSerializer;
            this.addressGenerator = addressGenerator;
            this.stateRoot = stateRoot;
            this.reserveUtxoService = reserveUtxoService;
        }

        public EstimateFeeResult EstimateFee(ScTxFeeEstimateRequest request)
        {
            Features.Wallet.Wallet wallet = this.walletManager.GetWallet(request.WalletName);

            HdAccount account = wallet.GetAccount(request.AccountName);

            if (account == null)
                return EstimateFeeResult.Failure(AccountNotInWalletError, $"No account with the name '{request.AccountName}' could be found.");

            HdAddress senderAddress = account.GetCombinedAddresses().FirstOrDefault(x => x.Address == request.Sender);
            if (senderAddress == null)
                return EstimateFeeResult.Failure(SenderNotInWalletError, $"The given address {request.Sender} was not found in the wallet.");

            if (!this.CheckBalance(senderAddress.Address))
                return EstimateFeeResult.Failure(InsufficientBalanceError, SenderNoBalanceError);

            (List<OutPoint> selectedInputs, string message) = SelectInputs(request.WalletName, request.Sender, request.Outpoints);
            if (!string.IsNullOrEmpty(message))
                return EstimateFeeResult.Failure(InvalidOutpointsError, message);

            var recipients = new List<Recipient>();
            foreach (RecipientModel recipientModel in request.Recipients)
            {
                var bitcoinAddress = BitcoinAddress.Create(recipientModel.DestinationAddress, this.network);

                // If it's a potential SC address, check if it's a contract.
                if (bitcoinAddress is BitcoinPubKeyAddress bitcoinPubKeyAddress)
                {
                    var address = new uint160(bitcoinPubKeyAddress.Hash.ToBytes());

                    if (this.stateRoot.IsExist(address))
                    {
                        return EstimateFeeResult.Failure(TransferFundsToContractError,
                            $"The recipient address {recipientModel.DestinationAddress} is a contract. Transferring funds directly to a contract is not supported.");
                    }
                }

                recipients.Add(new Recipient
                {
                    ScriptPubKey = bitcoinAddress.ScriptPubKey,
                    Amount = recipientModel.Amount
                });
            }

            // Build context
            var context = new TransactionBuildContext(this.network)
            {
                AccountReference = new WalletAccountReference(request.WalletName, request.AccountName),
                MinConfirmations = MinConfirmationsAllChecks,
                Shuffle = false,
                OpReturnData = request.OpReturnData,
                OpReturnAmount = string.IsNullOrEmpty(request.OpReturnAmount) ? null : Money.Parse(request.OpReturnAmount),
                SelectedInputs = selectedInputs,
                AllowOtherInputs = false,
                Recipients = recipients,
                ChangeAddress = senderAddress,

                // Unique for fee estimation
                TransactionFee = null,
                FeeType = FeeParser.Parse(request.FeeType),
                Sign = false,
            };

            Money fee = this.walletTransactionHandler.EstimateFee(context);

            return EstimateFeeResult.Success(fee);
        }

        public BuildContractTransactionResult BuildTx(BuildContractTransactionRequest request)
        {
            Features.Wallet.Wallet wallet = this.walletManager.GetWallet(request.WalletName);

            HdAccount account = wallet.GetAccount(request.AccountName);

            if (account == null)
                return BuildContractTransactionResult.Failure(AccountNotInWalletError, $"No account with the name '{request.AccountName}' could be found.");

            HdAddress senderAddress = account.GetCombinedAddresses().FirstOrDefault(x => x.Address == request.Sender);
            if (senderAddress == null)
                return BuildContractTransactionResult.Failure(SenderNotInWalletError, $"The given address {request.Sender} was not found in the wallet.");

            if (!this.CheckBalance(senderAddress.Address))
                return BuildContractTransactionResult.Failure(InsufficientBalanceError, SenderNoBalanceError);

            (List<OutPoint> selectedInputs, string message) = SelectInputs(request.WalletName, request.Sender, request.Outpoints);
            if (!string.IsNullOrEmpty(message))
                return BuildContractTransactionResult.Failure(InvalidOutpointsError, message);

            var recipients = new List<Recipient>();
            foreach (RecipientModel recipientModel in request.Recipients)
            {
                var bitcoinAddress = BitcoinAddress.Create(recipientModel.DestinationAddress, this.network);

                // If it's a potential SC address, check if it's a contract.
                if (bitcoinAddress is BitcoinPubKeyAddress bitcoinPubKeyAddress)
                {
                    var address = new uint160(bitcoinPubKeyAddress.Hash.ToBytes());

                    if (this.stateRoot.IsExist(address))
                    {
                        return BuildContractTransactionResult.Failure(TransferFundsToContractError,
                            $"The recipient address {recipientModel.DestinationAddress} is a contract. Transferring funds directly to a contract is not supported.");
                    }
                }

                recipients.Add(new Recipient
                {
                    ScriptPubKey = bitcoinAddress.ScriptPubKey,
                    Amount = recipientModel.Amount
                });
            }

            var context = new TransactionBuildContext(this.network)
            {
                AccountReference = new WalletAccountReference(request.WalletName, request.AccountName),
                TransactionFee = string.IsNullOrEmpty(request.FeeAmount) ? null : Money.Parse(request.FeeAmount),
                MinConfirmations = MinConfirmationsAllChecks,
                Shuffle = false,
                OpReturnData = request.OpReturnData,
                OpReturnAmount = string.IsNullOrEmpty(request.OpReturnAmount) ? null : Money.Parse(request.OpReturnAmount),
                WalletPassword = request.Password,
                SelectedInputs = selectedInputs,
                AllowOtherInputs = false,
                Recipients = recipients,
                ChangeAddress = senderAddress
            };

            Transaction transaction = this.walletTransactionHandler.BuildTransaction(context);

            var model = new WalletBuildTransactionModel
            {
                Hex = transaction.ToHex(),
                Fee = context.TransactionFee,
                TransactionId = transaction.GetHash()
            };

            return BuildContractTransactionResult.Success(model);
        }

        /// <summary>
        /// Parses elements until the ',' (separator) or ']' (ending) special character is encountered.
        /// Single quotes are used to enclose text regions to preserve whitespace or to make ']' or ',' non-special.
        /// </summary>
        /// <param name="param">The string to parse.</param>
        /// <param name="position">The position in the string following the '[' character.</param>
        /// <returns>The elements as a string array.</returns>
        /// <remarks>
        /// Single quotes are removed from elements and double single quotes are converted to literal single quotes.
        /// </remarks>
        public static string[] ParseArray(string param, ref int position)
        {
            var elements = new List<string>();
            int elementStart = position;
            bool quoted = false;

            for (; position < param.Length; position++)
            {
                if (param[position] == '\'')
                {
                    quoted = !quoted;
                }
                else if (!quoted && (param[position] == ',' || param[position] == ']'))
                {
                    string placeHolder = "\x0";

                    // Extract the element.
                    // Replace double-single-quotes with a placeholder. Remove single-quotes. Replace the placeholder with a single-quote.
                    string element = param.Substring(elementStart, position - elementStart).Trim().Replace("''", placeHolder).Replace("'", "").Replace(placeHolder, "'");
                    elements.Add(element);
                    if (param[position] == ']')
                        return elements.ToArray();
                    elementStart = position + 1;
                }
            }

            return null;
        }

        public static string ConvertParameter(ContractPrimitiveSerializer cpSerializer, MethodParameterStringSerializer mpSerializer, string param)
        {
            const char firstNumericDigit = '0';
            const char lastNumericDigit = '9';
            const int base10Multiplier = 10;
            const int dataTypeNotSupplied = 0;

            // Parse the method parameter data type that determines the type of array to create.
            int methodParameterDataType = dataTypeNotSupplied;
            int position = 0;
            for (; position < param.Length && param[position] >= firstNumericDigit && param[position] <= lastNumericDigit; position++)
            {
                int digitValue = param[position] - firstNumericDigit;
                methodParameterDataType = methodParameterDataType * base10Multiplier + digitValue;
            }

            try
            {
                // If this parameter is not an array then ignore it.
                if (position >= param.Length || param[position++] != '[')
                    return param;

                // The 'position' should now be set to the first character following '['.

                // If the type is omitted assume its a string.
                if (methodParameterDataType == dataTypeNotSupplied)
                    methodParameterDataType = (int)MethodParameterDataType.String;

                // Validate type.
                var dummy = (MethodParameterDataType)methodParameterDataType;

                // Parse the array.
                // The 'position' should now be set to the first character following '['.
                var elements = ParseArray(param, ref position);
                if (elements != null && param.Substring(position).Trim() == "]")
                {
                    object[] arrayElements = mpSerializer.Deserialize(elements.Select(e => $"{methodParameterDataType}#{e}").ToArray());

                    var serializedArray = cpSerializer.Serialize(arrayElements);

                    return $"{(int)MethodParameterDataType.ByteArray}#{BitConverter.ToString(serializedArray).Replace("-", "")}";
                }
            }
            catch (Exception)
            {
            }

            throw new Exception($"Parameter '{param}' has an invalid array syntax at character {position}.");
        }

        /// <summary>
        /// Replaces parameters of the form "N1[element1,element2,element3,...]" with byte arrays of the form "N2#byte-array-in-hex",
        /// where N1 and N2 are the integer values of the <see cref="MethodParameterDataType"/>, with N2 being 10 (byte array).
        /// </summary>
        /// <param name="network">The network.</param>
        /// <param name="parameters">A list of parameters that may contain arrays.</param>
        /// <returns>The input parameters with arrays replaced with byte arrays.</returns>
        /// <remarks>The encoded byte arrays are suitable for deserializing as arrays of the specified type (N1 in this example)
        /// by using <see cref="ContractPrimitiveSerializer.Deserialize{T}(byte[])"></see>.</remarks>
        private static string[] ReplaceArraysWithByteArrays(Network network, string[] parameters)
        {
            if (parameters == null)
                return null;

            var cpSerializer = new ContractPrimitiveSerializer(network);
            var mpSerializer = new MethodParameterStringSerializer(network);

            // Replace arrays with byte arrays.
            for (int i = 0; i < parameters.Length; i++)
                parameters[i] = ConvertParameter(cpSerializer, mpSerializer, parameters[i]);

            return parameters;
        }

        public BuildCallContractTransactionResponse BuildCallTx(BuildCallContractTransactionRequest request)
        {
            if (!this.CheckBalance(request.Sender))
                return BuildCallContractTransactionResponse.Failed(SenderNoBalanceError);

            (List<OutPoint> selectedInputs, string message) = SelectInputs(request.WalletName, request.Sender, request.Outpoints);
            if (!string.IsNullOrEmpty(message))
                return BuildCallContractTransactionResponse.Failed(message);

            uint160 addressNumeric = request.ContractAddress.ToUint160(this.network);

            ContractTxData txData;
            if (request.Parameters != null && request.Parameters.Any())
            {
                try
                {
                    request.Parameters = ReplaceArraysWithByteArrays(this.network, request.Parameters);

                    object[] methodParameters = this.methodParameterStringSerializer.Deserialize(request.Parameters);
                    txData = new ContractTxData(ReflectionVirtualMachine.VmVersion, (Stratis.SmartContracts.RuntimeObserver.Gas)request.GasPrice, (Stratis.SmartContracts.RuntimeObserver.Gas)request.GasLimit, addressNumeric, request.MethodName, methodParameters);
                }
                catch (MethodParameterStringSerializerException exception)
                {
                    return BuildCallContractTransactionResponse.Failed(exception.Message);
                }
            }
            else
            {
                txData = new ContractTxData(ReflectionVirtualMachine.VmVersion, (Stratis.SmartContracts.RuntimeObserver.Gas)request.GasPrice, (Stratis.SmartContracts.RuntimeObserver.Gas)request.GasLimit, addressNumeric, request.MethodName);
            }

            HdAddress senderAddress = null;
            if (!string.IsNullOrWhiteSpace(request.Sender))
            {
                Features.Wallet.Wallet wallet = this.walletManager.GetWallet(request.WalletName);
                HdAccount account = wallet.GetAccount(request.AccountName);
                if (account == null)
                    return BuildCallContractTransactionResponse.Failed($"No account with the name '{request.AccountName}' could be found.");

                senderAddress = account.GetCombinedAddresses().FirstOrDefault(x => x.Address == request.Sender);
            }

            ulong totalFee = (request.GasPrice * request.GasLimit) + Money.Parse(request.FeeAmount);
            var context = new TransactionBuildContext(this.network)
            {
                AccountReference = new WalletAccountReference(request.WalletName, request.AccountName),
                TransactionFee = totalFee,
                ChangeAddress = senderAddress,
                SelectedInputs = selectedInputs,
                MinConfirmations = MinConfirmationsAllChecks,
                WalletPassword = request.Password,
                Recipients = new[] { new Recipient { Amount = request.Amount, ScriptPubKey = new Script(this.callDataSerializer.Serialize(txData)) } }.ToList()
            };

            try
            {
                Transaction transaction = this.walletTransactionHandler.BuildTransaction(context);
                return BuildCallContractTransactionResponse.Succeeded(request.MethodName, transaction, context.TransactionFee);
            }
            catch (Exception exception)
            {
                return BuildCallContractTransactionResponse.Failed(exception.Message);
            }
        }

        public BuildCreateContractTransactionResponse BuildCreateTx(BuildCreateContractTransactionRequest request)
        {
            if (!this.CheckBalance(request.Sender))
                return BuildCreateContractTransactionResponse.Failed(SenderNoBalanceError);

            (List<OutPoint> selectedInputs, string message) = this.SelectInputs(request.WalletName, request.Sender, request.Outpoints);
            if (!string.IsNullOrEmpty(message))
                return BuildCreateContractTransactionResponse.Failed(message);

            ContractTxData txData;
            if (request.Parameters != null && request.Parameters.Any())
            {
                try
                {
                    request.Parameters = ReplaceArraysWithByteArrays(this.network, request.Parameters);

                    object[] methodParameters = this.methodParameterStringSerializer.Deserialize(request.Parameters);
                    txData = new ContractTxData(ReflectionVirtualMachine.VmVersion, (Stratis.SmartContracts.RuntimeObserver.Gas)request.GasPrice, (Stratis.SmartContracts.RuntimeObserver.Gas)request.GasLimit, request.ContractCode.HexToByteArray(), methodParameters);
                }
                catch (MethodParameterStringSerializerException exception)
                {
                    return BuildCreateContractTransactionResponse.Failed(exception.Message);
                }
            }
            else
            {
                txData = new ContractTxData(ReflectionVirtualMachine.VmVersion, (Stratis.SmartContracts.RuntimeObserver.Gas)request.GasPrice, (Stratis.SmartContracts.RuntimeObserver.Gas)request.GasLimit, request.ContractCode.HexToByteArray());
            }

            HdAddress senderAddress = null;
            if (!string.IsNullOrWhiteSpace(request.Sender))
            {
                Features.Wallet.Wallet wallet = this.walletManager.GetWallet(request.WalletName);
                HdAccount account = wallet.GetAccount(request.AccountName);
                if (account == null)
                    return BuildCreateContractTransactionResponse.Failed($"No account with the name '{request.AccountName}' could be found.");

                senderAddress = account.GetCombinedAddresses().FirstOrDefault(x => x.Address == request.Sender);
            }

            ulong totalFee = (request.GasPrice * request.GasLimit) + Money.Parse(request.FeeAmount);
            var walletAccountReference = new WalletAccountReference(request.WalletName, request.AccountName);

            byte[] serializedTxData = this.callDataSerializer.Serialize(txData);

            Result<ContractTxData> deserialized = this.callDataSerializer.Deserialize(serializedTxData);

            // We also want to ensure we're sending valid data: AKA it can be deserialized.
            if (deserialized.IsFailure)
            {
                return BuildCreateContractTransactionResponse.Failed("Invalid data. If network requires code signing, check the code contains a signature.");
            }

            var recipient = new Recipient { Amount = request.Amount ?? "0", ScriptPubKey = new Script(serializedTxData) };
            var context = new TransactionBuildContext(this.network)
            {
                AccountReference = walletAccountReference,
                TransactionFee = totalFee,
                ChangeAddress = senderAddress,
                SelectedInputs = selectedInputs,
                MinConfirmations = MinConfirmationsAllChecks,
                WalletPassword = request.Password,
                Recipients = new[] { recipient }.ToList()
            };

            try
            {
                Transaction transaction = this.walletTransactionHandler.BuildTransaction(context);
                uint160 contractAddress = this.addressGenerator.GenerateAddress(transaction.GetHash(), 0);
                return BuildCreateContractTransactionResponse.Succeeded(transaction, context.TransactionFee, contractAddress.ToBase58Address(this.network));
            }
            catch (Exception exception)
            {
                return BuildCreateContractTransactionResponse.Failed(exception.Message);
            }
        }

        public ContractTxData BuildLocalCallTxData(LocalCallContractRequest request)
        {
            uint160 contractAddress = request.ContractAddress.ToUint160(this.network);

            if (request.Parameters != null && request.Parameters.Any())
            {
                object[] methodParameters = this.methodParameterStringSerializer.Deserialize(request.Parameters);

                return new ContractTxData(ReflectionVirtualMachine.VmVersion, (Stratis.SmartContracts.RuntimeObserver.Gas)request.GasPrice, (Stratis.SmartContracts.RuntimeObserver.Gas)request.GasLimit, contractAddress, request.MethodName, methodParameters);
            }

            return new ContractTxData(ReflectionVirtualMachine.VmVersion, (Stratis.SmartContracts.RuntimeObserver.Gas)request.GasPrice, (Stratis.SmartContracts.RuntimeObserver.Gas)request.GasLimit, contractAddress, request.MethodName);
        }

        private bool CheckBalance(string address)
        {
            AddressBalance addressBalance = this.walletManager.GetAddressBalance(address);
            return !(addressBalance.AmountConfirmed == 0 && addressBalance.AmountUnconfirmed == 0);
        }

        private (List<OutPoint> seletedInputs, string message) SelectInputs(string walletName, string sender, List<OutpointRequest> requestedOutpoints)
        {
            List<OutPoint> selectedInputs = this.walletManager.GetSpendableInputsForAddress(walletName, sender);
            if (!selectedInputs.Any())
                return (selectedInputs, "The wallet does not contain any spendable inputs.");

            if (requestedOutpoints != null && requestedOutpoints.Any())
            {
                selectedInputs = this.ReduceToRequestedInputs(requestedOutpoints, selectedInputs);
                if (!selectedInputs.Any())
                    return (selectedInputs, "An invalid list of request outpoints have been passed to the method, please ensure that the outpoints are spendable by the sender address.");
            }

            selectedInputs = FilterReservedInputs(selectedInputs);
            if (!selectedInputs.Any())
                return (selectedInputs, "All of the selected inputs are currently reserved, please try again in 60 seconds.");

            return (selectedInputs, null);
        }

        private List<OutPoint> FilterReservedInputs(List<OutPoint> selectedInputs)
        {
            var result = new List<OutPoint>();
            foreach (OutPoint input in selectedInputs)
            {
                if (!this.reserveUtxoService.IsUtxoReserved(input))
                    result.Add(input);
            }

            return result;
        }

        /// <summary>
        /// Reduces the selectedInputs to consist of only those asked for by the request, or leaves them the same if none were requested.
        /// </summary>
        /// <returns>The new list of outpoints.</returns>
        private List<OutPoint> ReduceToRequestedInputs(IReadOnlyList<OutpointRequest> requestedOutpoints, IReadOnlyList<OutPoint> selectedInputs)
        {
            var result = new List<OutPoint>(selectedInputs);

            // Convert outpointRequest to OutPoint
            IEnumerable<OutPoint> requestedOutPoints = requestedOutpoints.Select(outPointRequest => new OutPoint(new uint256(outPointRequest.TransactionId), outPointRequest.Index));

            for (int i = result.Count - 1; i >= 0; i--)
            {
                if (!requestedOutPoints.Contains(result[i]))
                    result.RemoveAt(i);
            }

            return result;
        }
    }
}