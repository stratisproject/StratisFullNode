using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Policy;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.Features.Wallet.Services;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.ColdStaking
{
    /// <summary>
    /// Contains modified implementations of the <see cref="IWalletService"/> methods suitable for cold staking.
    /// </summary>
    public sealed class ColdStakingWalletService : WalletService
    {
        public ColdStakingWalletService(
            ILoggerFactory loggerFactory, IWalletManager walletManager,
            IConsensusManager consensusManager, IWalletTransactionHandler walletTransactionHandler,
            IWalletSyncManager walletSyncManager, IConnectionManager connectionManager,
            Network network, ChainIndexer chainIndexer, IBroadcasterManager broadcasterManager,
            IDateTimeProvider dateTimeProvider, IUtxoIndexer utxoIndexer,
            IWalletFeePolicy walletFeePolicy, NodeSettings nodeSettings)
            : base(loggerFactory, walletManager, consensusManager, walletTransactionHandler, walletSyncManager, connectionManager, network, chainIndexer, broadcasterManager, dateTimeProvider, utxoIndexer, walletFeePolicy, nodeSettings)
        {
        }

        public override async Task<WalletBuildTransactionModel> OfflineSignRequest(OfflineSignRequest request, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                Transaction unsignedTransaction = this.network.CreateTransaction(request.UnsignedTransaction);

                uint256 originalTxId = unsignedTransaction.GetHash();

                var builder = new TransactionBuilder(this.network);
                var coins = new List<Coin>();
                var signingKeys = new List<ISecret>();

                ExtKey seedExtKey = this.walletManager.GetExtKey(new WalletAccountReference() { AccountName = request.WalletAccount, WalletName = request.WalletName }, request.WalletPassword);

                // Have to determine which private key to use for each UTXO being spent.
                bool coldStakingWithdrawal = false;
                foreach (UtxoDescriptor utxo in request.Utxos)
                {
                    Script scriptPubKey = Script.FromHex(utxo.ScriptPubKey);

                    coins.Add(new Coin(uint256.Parse(utxo.TransactionId), uint.Parse(utxo.Index), Money.Parse(utxo.Amount), scriptPubKey));

                    // Now try get the associated private key. We therefore need to determine the address that contains the UTXO.
                    string address;
                    if (scriptPubKey.IsScriptType(ScriptType.ColdStaking))
                    {
                        ColdStakingScriptTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey, out KeyId hotPubKeyHash, out KeyId coldPubKeyHash);

                        address = coldPubKeyHash.GetAddress(this.network).ToString();
                        coldStakingWithdrawal = true;
                    }
                    else
                    {
                        // We assume that if it wasn't a cold staking scriptPubKey then it must have been P2PKH.
                        address = scriptPubKey.GetDestinationAddress(this.network)?.ToString();

                        if (address == null)
                            throw new FeatureException(HttpStatusCode.BadRequest, "Could not resolve address.",
                                $"Could not resolve address from UTXO's scriptPubKey '{ scriptPubKey.ToHex() }'.");
                    }

                    var accounts = this.walletManager.GetAccounts(request.WalletName);
                    var addresses = accounts.SelectMany(hdAccount => hdAccount.GetCombinedAddresses());

                    HdAddress hdAddress = addresses.FirstOrDefault(a => a.Address == address || a.Bech32Address == address);

                    if (coldStakingWithdrawal && hdAddress == null)
                    {
                        var coldStakingManager = this.walletManager as ColdStakingManager;
                        var wallet = coldStakingManager.GetWallet(request.WalletName);
                        var coldAccount = coldStakingManager.GetColdStakingAccount(wallet, true);
                        var coldAccountAddresses = coldAccount.GetCombinedAddresses();
                        hdAddress = coldAccountAddresses.FirstOrDefault(a => a.Address == address || a.Bech32Address == address);
                    }

                    // It is possible that the address is outside the gap limit. So if it is not found we optimistically presume the address descriptors will fill in the missing information later.
                    if (hdAddress != null)
                    {
                        ExtKey addressExtKey = seedExtKey.Derive(new KeyPath(hdAddress.HdPath));
                        BitcoinExtKey addressPrivateKey = addressExtKey.GetWif(this.network);
                        signingKeys.Add(addressPrivateKey);
                    }
                }

                // Address descriptors are 'easier' to look the private key up against if provided, but may not always be available.
                foreach (AddressDescriptor address in request.Addresses)
                {
                    ExtKey addressExtKey = seedExtKey.Derive(new KeyPath(address.KeyPath));
                    BitcoinExtKey addressPrivateKey = addressExtKey.GetWif(this.network);
                    signingKeys.Add(addressPrivateKey);
                }

                // Offline cold staking transaction handling. We check both the offline setup and the offline withdrawal cases here.
                if (unsignedTransaction.Outputs.Any(o => o.ScriptPubKey.IsScriptType(ScriptType.ColdStaking)) || coldStakingWithdrawal)
                {
                    // This will always be added in 'cold' mode if we are processing an offline signing request.
                    builder.Extensions.Add(new ColdStakingBuilderExtension(false));
                }

                builder.AddCoins(coins);
                builder.AddKeys(signingKeys.ToArray());
                builder.SignTransactionInPlace(unsignedTransaction);

                if (!builder.Verify(unsignedTransaction, out TransactionPolicyError[] errors))
                {
                    throw new FeatureException(HttpStatusCode.BadRequest, "Failed to validate signed transaction.",
                        $"Failed to validate signed transaction '{unsignedTransaction.GetHash()}' from offline request '{originalTxId}'.");
                }

                var builtTransactionModel = new WalletBuildTransactionModel() { TransactionId = unsignedTransaction.GetHash(), Hex = unsignedTransaction.ToHex(), Fee = request.Fee };

                return builtTransactionModel;
            }, cancellationToken);
        }
    }
}
