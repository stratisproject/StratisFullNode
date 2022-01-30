using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.ABI;
using Nethereum.Contracts;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.RPC.Eth.Blocks;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Nethereum.Web3.Accounts.Managed;
using NLog;

namespace Stratis.Bitcoin.Features.Interop.ETHClient
{
    public interface IETHClient
    {
        /// <summary>
        /// Creates the filter that the RPC interface uses to listen for events against the desired contract.
        /// In this case the filter is specifically listening for Transfer events emitted by the wrapped strax
        /// contract.
        /// </summary>
        Task CreateTransferEventFilterAsync();

        Task<Transaction> GetTransactionAsync(string transactionHash);

        Task<SubmitTransactionFunction> GetSubmitTransactionAsync(string transactionHash);

        Task<ConfirmTransactionFunction> GetConfirmTransactionAsync(string transactionHash);

        Task<MintFunction> GetMintTransactionAsync(string transactionHash);

        Task<BurnFunction> GetBurnTransactionAsync(string transactionHash);

        Task<BlockWithTransactions> GetBlockAsync(BigInteger blockNumber);

        Task<List<(string TransactionHash, BurnFunction Burn)>> GetBurnsFromBlock(BlockWithTransactions block);

        List<(string TransactionHash, string TransferContractAddress, TransferFunction Transfer)> GetTransfersFromBlock(BlockWithTransactions block, HashSet<string> tokens);

        /// <summary>
        /// Queries the previously created event filter for any new events matching the filter criteria.
        /// </summary>
        /// <returns>A list of event logs.</returns>
        Task<List<EventLog<TransferEventDTO>>> GetTransferEventsForWrappedStraxAsync();

        /// <summary>
        /// Retrieves the STRAX address that was recorded in the wrapped STRAX contract when a given account
        /// burnt funds. The destination address must have been provided as a parameter to the burn() method
        /// invocation and only one address at a time can be associated with each account.
        /// </summary>
        /// <param name="address">The Ethereum account to retrieve the destination STRAX address for.</param>
        /// <returns>The STRAX address associated with the provided Ethereum account.</returns>
        Task<string> GetDestinationAddressAsync(string address);

        /// <summary>
        /// Retrieves the current block height of the Ethereum node.
        /// </summary>
        Task<BigInteger> GetBlockHeightAsync();

        /// <summary>
        /// Retrieves the balance for an Ethereum account.
        /// </summary>
        /// <param name="address">The Ethereum account to retrieve the destination balance for.</param>
        /// <returns>The balance of the account in wei.</returns>
        Task<BigInteger> GetBalanceAsync(string address);

        /// <summary>
        /// Submits a transaction to the multisig wallet contract, to enable it to be separately confirmed by a quorum of the multisig wallet owners.
        /// </summary>
        /// <param name="destination">The account that the transaction is being sent to after confirmation. For wSTRAX operations this will typically be the wSTRAX ERC20 contract address.</param>
        /// <param name="value">The amount that is being sent. For wSTRAX operations this is typically zero, as the balance changes are encoded within the additional data.</param>
        /// <param name="data">Additional transaction data. This is encoded in accordance with the applicable contract's ABI.</param>
        /// <param name="gasPrice">The gas price to be used for the transaction, in gwei.</param>
        /// <returns>Returns the hash and transactionId of the submission transaction.</returns>
        Task<MultisigTransactionIdentifiers> SubmitTransactionAsync(string destination, BigInteger value, string data, int gasPrice);

        /// <summary>
        /// Confirms a multisig wallet transaction.
        /// </summary>
        /// <remarks>Once a sufficient threshold of confirmations is reached, the contract will automatically execute the saved transaction.</remarks>
        /// <param name="transactionId">The transactionId of an existing transaction stored in the multisig wallet contract.</param>
        /// <param name="gasPrice">The gas price to be used for the transaction, in gwei.</param>
        /// <returns>The hash of the confirmation transaction.</returns>
        Task<string> ConfirmTransactionAsync(BigInteger transactionId, int gasPrice);

        /// <summary>
        /// Retrieve the number of confirmations a given transaction currently has in the multisig wallet contract.
        /// </summary>
        /// <param name="transactionId">The numeric identifier of the transaction stored inside the multisig contract.</param>
        /// <returns>The number of confirmations.</returns>
        Task<BigInteger> GetMultisigConfirmationCountAsync(BigInteger transactionId);

        Task<(BigInteger ConfirmationCount, string BlockHash)> GetConfirmationsAsync(string transactionHash);

        /// <summary>
        /// Gets the list of owners for the multisig wallet contract.
        /// </summary>
        /// <returns>The list of owner accounts.</returns>
        Task<List<string>> GetOwnersAsync();

        /// <summary>
        /// Checks if the given address confirmed the given multisig transaction.
        /// </summary>
        /// <param name="transactionId">The identifier of the transaction.</param>
        /// <param name="address">The address to check the confirmation status of.</param>
        /// <returns>True if the address has confirmed the transaction in question.</returns>
        Task<bool> AddressConfirmedTransactionAsync(BigInteger transactionId, string address);

        /// <summary>
        /// Gets a transaction out of the transactions mapping on the contract and decodes it.
        /// </summary>
        /// <param name="transactionId">The identifier of the transaction to retrieve.</param>
        /// <returns>A decoded multisig transaction object.</returns>
        Task<TransactionDTO> GetMultisigTransactionAsync(BigInteger transactionId);

        /// <summary>
        /// Gets a transaction out of the transactions mapping on the contract without decoding it.
        /// </summary>
        /// <param name="transactionId">The identifier of the transaction to retrieve.</param>
        /// <returns>Raw hex data.</returns>
        Task<string> GetRawMultisigTransactionAsync(BigInteger transactionId);

        /// <summary>
        /// Retrieves the wSTRAX balance associated with an account.
        /// </summary>
        /// <param name="addressToQuery">The account to retrieve the ERC20 balance of.</param>
        /// <returns>The balance of the account.</returns>
        Task<BigInteger> GetErc20BalanceAsync(string addressToQuery);

        /// <summary>
        /// Retrieves a string from the Key Value Store contract.
        /// </summary>
        /// <remarks>Submitted key-value pairs are not fully private to the node that submitted them, and can be
        /// read by any participant on the associated network if they have knowledge of the submitter's address
        /// as well as the key.</remarks>
        /// <param name="address">The address of the node that originally submitted the key-value pair.</param>
        /// <param name="key">The key of the key-value pair to be retrieved.</param>
        /// <returns>The string value stored against the specified key by the specified submitting node.</returns>
        Task<string> GetKeyValueStoreAsync(string address, string key);

        /// <summary>
        /// Stores a string value in the Key Value Store contract.
        /// </summary>
        /// <remarks>It is not possible to specify the 'source address', it is automatically set to
        /// the address of the submitting node's wallet. It is also therefore not possible to overwrite
        /// another node's submitted values, inadvertently or otherwise.</remarks>
        /// <param name="key">The key to store the value against.</param>
        /// <param name="value">The string value to be stored.</param>
        /// <param name="gasPrice">The gas price to be used for the transaction, in gwei.</param>
        /// <returns>The transaction ID of the resulting contract call transaction.</returns>
        Task<string> SetKeyValueStoreAsync(string key, string value, BigInteger gasPrice);

        /// <summary>
        /// Returns the encoded form of transaction data that calls the transfer(address, uint256) method on the WrappedStrax contract.
        /// This is exactly the contents of the 'data' field in a normal transaction.
        /// This encoded data is required for submitting a transaction to the multisig contract.
        /// </summary>
        /// <param name="address">The address to transfer a quantity of the wSTRAX token to.</param>
        /// <param name="amount">The amount (in wei) of tokens to be transferred.</param>
        /// <returns>The hex data of the encoded parameters.</returns>
        string EncodeTransferParams(string address, BigInteger amount);

        /// <summary>
        /// Constructs the data field for a transaction invoking the mint() method of an ERC20 contract that implements it.
        /// The actual transaction will be sent to the multisig wallet contract as it is the contract that needs to execute the transaction.
        /// </summary>
        /// <param name="address">The account that needs tokens to be minted into it (not the address of the multisig contract or the wrapped STRAX contract)</param>
        /// <param name="amount">The number of tokens to be minted. This is denominated in wei.</param>
        /// <returns>The hex data of the encoded parameters.</returns>
        string EncodeMintParams(string address, BigInteger amount);

        /// <summary>
        /// Constructs the data field for a transaction invoking the burn() method of an ERC20 contract that implements it.
        /// The actual transaction will be sent to the multisig wallet contract as it is the contract that needs to execute the transaction.
        /// </summary>
        /// <param name="amount">The number of tokens to be minted. This is denominated in wei.</param>
        /// <param name="straxAddress">The destination address on the STRAX chain that the equivalent value of the burnt funds will be sent to.</param>
        /// <returns>The hex data of the encoded parameters.</returns>
        string EncodeBurnParams(BigInteger amount, string straxAddress);

        /// <summary>
        /// Constructs the data field for a transaction invoking the addOwner() method of the multisig wallet contract.
        /// </summary>
        /// <param name="newOwnerAddress">The account of the new owner to be added to the multisig owners list.</param>
        /// <returns>The hex data of the encoded parameters.</returns>
        string EncodeAddOwnerParams(string newOwnerAddress);

        /// <summary>
        /// Constructs the data field for a transaction invoking the removeOwner() method of the multisig wallet contract.
        /// </summary>
        /// <param name="existingOwnerAddress">The account of the existing owner to be removed from the multisig owners list.</param>
        /// <returns>The hex data of the encoded parameters.</returns>
        string EncodeRemoveOwnerParams(string existingOwnerAddress);

        /// <summary>
        /// Constructs the data field for a transaction invoking the changeRequirement() method of the multisig wallet contract.
        /// </summary>
        /// <param name="requirement">The new threshold for confirmations for a multisig transaction to be executed.</param>
        /// <returns>The hex data of the encoded parameters.</returns>
        string EncodeChangeRequirementParams(BigInteger requirement);
    }

    public class ETHClient : IETHClient
    {
        protected ETHInteropSettings settings;
        protected Web3 web3;
        private readonly ILogger logger;
        protected Event<TransferEventDTO> transferEventHandler;
        protected NewFilterInput filterAllTransferEventsForContract;
        protected HexBigInteger filterId;

        public const string ZeroAddress = "0x0000000000000000000000000000000000000000";

        public ETHClient(InteropSettings interopSettings)
        {
            this.SetupConfiguration(interopSettings);

            if (!this.settings.InteropEnabled)
                return;

            var account = new ManagedAccount(this.settings.Account, this.settings.Passphrase);

            // TODO: Support loading offline accounts from keystore JSON directly?
            this.web3 = !string.IsNullOrWhiteSpace(this.settings.ClientUrl) ? new Web3(account, this.settings.ClientUrl) : new Web3(account);

            this.logger = LogManager.GetCurrentClassLogger();
        }

        protected virtual void SetupConfiguration(InteropSettings interopSettings)
        {
            this.settings = interopSettings.ETHSettings;
        }

        /// <inheritdoc />
        public async Task CreateTransferEventFilterAsync()
        {
            this.transferEventHandler = this.web3.Eth.GetEvent<TransferEventDTO>(this.settings.WrappedStraxContractAddress);
            this.filterAllTransferEventsForContract = this.transferEventHandler.CreateFilterInput();
            this.filterId = await this.transferEventHandler.CreateFilterAsync(this.filterAllTransferEventsForContract).ConfigureAwait(false);
        }

        public async Task<Transaction> GetTransactionAsync(string transactionHash)
        {
            return await this.web3.Eth.Transactions.GetTransactionByHash.SendRequestAsync(transactionHash).ConfigureAwait(false);
        }

        public async Task<SubmitTransactionFunction> GetSubmitTransactionAsync(string transactionHash)
        {
            Transaction tx = await GetTransactionAsync(transactionHash).ConfigureAwait(false);

            return tx.DecodeTransactionToFunctionMessage<SubmitTransactionFunction>();
        }

        public async Task<ConfirmTransactionFunction> GetConfirmTransactionAsync(string transactionHash)
        {
            Transaction tx = await GetTransactionAsync(transactionHash).ConfigureAwait(false);

            return tx.DecodeTransactionToFunctionMessage<ConfirmTransactionFunction>();
        }

        public async Task<MintFunction> GetMintTransactionAsync(string transactionHash)
        {
            Transaction tx = await GetTransactionAsync(transactionHash).ConfigureAwait(false);

            return tx.DecodeTransactionToFunctionMessage<MintFunction>();
        }

        public async Task<BurnFunction> GetBurnTransactionAsync(string transactionHash)
        {
            Transaction tx = await GetTransactionAsync(transactionHash).ConfigureAwait(false);

            return tx.DecodeTransactionToFunctionMessage<BurnFunction>();
        }

        public async Task<BlockWithTransactions> GetBlockAsync(BigInteger blockNumber)
        {
            return await this.web3.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(new HexBigInteger(blockNumber)).ConfigureAwait(false);
        }

        public async Task<List<(string TransactionHash, BurnFunction Burn)>> GetBurnsFromBlock(BlockWithTransactions block)
        {
            var burns = new List<(string TransactionHash, BurnFunction Burn)>();

            foreach (Transaction tx in block.Transactions)
            {
                if (!tx.IsTo(this.settings.WrappedStraxContractAddress))
                    continue;

                BurnFunction burn;

                try
                {
                    burn = tx.DecodeTransactionToFunctionMessage<BurnFunction>();
                }
                catch
                {
                    continue;
                }

                if (burn.Amount == BigInteger.Zero)
                {
                    // Ignoring zero-valued burn transaction.
                    continue;
                }

                if (burn.Amount < BigInteger.Zero)
                {
                    // Ignoring negative-valued burn transaction.
                    continue;
                }

                burns.Add((tx.TransactionHash, burn));
            }

            return burns;
        }

        public List<(string TransactionHash, string TransferContractAddress, TransferFunction Transfer)> GetTransfersFromBlock(BlockWithTransactions block, HashSet<string> tokens)
        {
            var transfers = new List<(string TransactionHash, string TransferContractAddress, TransferFunction Transfer)>();

            foreach (Transaction tx in block.Transactions)
            {
                this.logger.Debug($"Checking tx '{tx.TransactionHash}'");

                // The transfer call obviously isn't made against the federation's multisig wallet contract itself. So we need to check against the list of previously added token contracts.
                if (!tokens.Contains(tx.To))
                    continue;

                // TODO: Need to abstract out the needed and common fields rather than storing the function - there will be multiple 'Transfer' equivalents in the various token contract standards
                TransferFunction transfer;

                try
                {
                    this.logger.Debug($"Decoding tx '{tx.TransactionHash}'");
                    transfer = tx.DecodeTransactionToFunctionMessage<TransferFunction>();
                }
                catch
                {
                    continue;
                }

                if (transfer.To != this.settings.MultisigWalletAddress)
                {
                    // Ignoring transfers that are made to any address except the federation's multisig wallet.
                    continue;
                }

                if (transfer.TokenAmount == BigInteger.Zero)
                {
                    // Ignoring zero-valued transfer.
                    continue;
                }

                if (transfer.TokenAmount < BigInteger.Zero)
                {
                    // Ignoring negative-valued transfer.
                    continue;
                }

                transfers.Add((tx.TransactionHash, tx.To, transfer));
            }

            return transfers;
        }

        /// <inheritdoc />
        public async Task<List<EventLog<TransferEventDTO>>> GetTransferEventsForWrappedStraxAsync()
        {
            try
            {
                // Note: this will only return events from after the filter is created.
                return await this.transferEventHandler.GetFilterChanges(this.filterId).ConfigureAwait(false);
            }
            catch (RpcResponseException)
            {
                // If the filter is no longer available it may need to be re-created.
                await this.CreateTransferEventFilterAsync().ConfigureAwait(false);
            }

            return await this.transferEventHandler.GetFilterChanges(this.filterId).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<string> GetDestinationAddressAsync(string address)
        {
            return await WrappedStrax.GetDestinationAddressAsync(this.web3, this.settings.WrappedStraxContractAddress, address).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<BigInteger> GetBlockHeightAsync()
        {
            var blockNumberHandler = new EthBlockNumber(this.web3.Client);
            HexBigInteger block = await blockNumberHandler.SendRequestAsync().ConfigureAwait(false);

            return block.Value;
        }

        /// <inheritdoc />
        public async Task<BigInteger> GetBalanceAsync(string address)
        {

            HexBigInteger balance = await this.web3.Eth.GetBalance.SendRequestAsync(address).ConfigureAwait(false);

            return balance.Value;
        }

        /// <inheritdoc />
        public async Task<MultisigTransactionIdentifiers> SubmitTransactionAsync(string destination, BigInteger value, string data, int gasPrice)
        {
            return await MultisigWallet.SubmitTransactionAsync(this.web3, this.settings.MultisigWalletAddress, destination, value, data, this.settings.GasLimit, gasPrice).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<string> ConfirmTransactionAsync(BigInteger transactionId, int gasPrice)
        {
            return await MultisigWallet.ConfirmTransactionAsync(this.web3, this.settings.MultisigWalletAddress, transactionId, this.settings.GasLimit, gasPrice).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<BigInteger> GetMultisigConfirmationCountAsync(BigInteger transactionId)
        {
            return await MultisigWallet.GetConfirmationCountAsync(this.web3, this.settings.MultisigWalletAddress, transactionId).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<string>> GetOwnersAsync()
        {
            return await MultisigWallet.GetOwnersAsync(this.web3, this.settings.MultisigWalletAddress).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<TransactionDTO> GetMultisigTransactionAsync(BigInteger transactionId)
        {
            return await MultisigWallet.GetTransactionAsync(this.web3, this.settings.MultisigWalletAddress, transactionId).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> AddressConfirmedTransactionAsync(BigInteger transactionId, string address)
        {
            ConfirmationsDTO result = await MultisigWallet.AddressConfirmedTransactionAsync(this.web3, this.settings.MultisigWalletAddress, transactionId, address).ConfigureAwait(false);

            return result.Confirmed;
        }

        /// <inheritdoc />
        public async Task<string> GetRawMultisigTransactionAsync(BigInteger transactionId)
        {
            return await MultisigWallet.GetRawTransactionAsync(this.web3, this.settings.MultisigWalletAddress, transactionId).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<(BigInteger ConfirmationCount, string BlockHash)> GetConfirmationsAsync(string transactionHash)
        {
            Transaction transaction = await this.web3.Eth.Transactions.GetTransactionByHash.SendRequestAsync(transactionHash).ConfigureAwait(false);

            if (transaction == null || transaction.BlockNumber == null)
                return (0, string.Empty);

            BigInteger currentBlockHeight = await this.GetBlockHeightAsync().ConfigureAwait(false);

            BigInteger confirmations = currentBlockHeight - transaction.BlockNumber.Value;

            return (confirmations > 0 ? confirmations : BigInteger.Zero, transaction.BlockHash);
        }

        /// <inheritdoc />
        public async Task<BigInteger> GetErc20BalanceAsync(string addressToQuery)
        {
            return await WrappedStrax.GetErc20BalanceAsync(this.web3, this.settings.WrappedStraxContractAddress, addressToQuery).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<string> GetKeyValueStoreAsync(string address, string key)
        {
            return await KVStore.GetAsync(this.web3, this.settings.KeyValueStoreContractAddress, address, key).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<string> SetKeyValueStoreAsync(string key, string value, BigInteger gasPrice)
        {
            return await KVStore.SetAsync(this.web3, this.settings.KeyValueStoreContractAddress, key, value, this.settings.GasLimit, gasPrice).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public string EncodeTransferParams(string address, BigInteger amount)
        {
            // TODO: Extract this directly from the ABI
            const string TransferMethod = "a9059cbb";

            var abiEncode = new ABIEncode();
            string result = TransferMethod + abiEncode.GetABIEncoded(new ABIValue("address", address), new ABIValue("uint256", amount)).ToHex();

            return result;
        }

        /// <inheritdoc />
        public string EncodeMintParams(string address, BigInteger amount)
        {
            // TODO: Extract this directly from the ABI
            const string MintMethod = "40c10f19";

            var abiEncode = new ABIEncode();
            string result = MintMethod + abiEncode.GetABIEncoded(new ABIValue("address", address), new ABIValue("uint256", amount)).ToHex();

            return result;
        }

        /// <inheritdoc />
        public string EncodeBurnParams(BigInteger amount, string straxAddress)
        {
            // TODO: Extract this directly from the ABI
            const string BurnMethod = "7641e6f3";

            var abiEncode = new ABIEncode();
            string result = BurnMethod + abiEncode.GetABIEncoded(new ABIValue("uint256", amount), new ABIValue("string", straxAddress)).ToHex();

            return result;
        }

        /// <inheritdoc />
        public string EncodeAddOwnerParams(string newOwnerAddress)
        {
            // TODO: Extract this directly from the ABI
            const string AddOwnerMethod = "7065cb48";

            var abiEncode = new ABIEncode();
            string result = AddOwnerMethod + abiEncode.GetABIEncoded(new ABIValue("address", newOwnerAddress)).ToHex();

            return result;
        }

        /// <inheritdoc />
        public string EncodeRemoveOwnerParams(string existingOwnerAddress)
        {
            // TODO: Extract this directly from the ABI
            const string RemoveOwnerMethod = "173825d9";

            var abiEncode = new ABIEncode();
            string result = RemoveOwnerMethod + abiEncode.GetABIEncoded(new ABIValue("address", existingOwnerAddress)).ToHex();

            return result;
        }

        /// <inheritdoc />
        public string EncodeChangeRequirementParams(BigInteger requirement)
        {
            // TODO: Extract this directly from the ABI
            const string ChangeRequirementMethod = "ba51a6df";

            var abiEncode = new ABIEncode();
            string result = ChangeRequirementMethod + abiEncode.GetABIEncoded(new ABIValue("uint256", requirement)).ToHex();

            return result;
        }
    }
}
