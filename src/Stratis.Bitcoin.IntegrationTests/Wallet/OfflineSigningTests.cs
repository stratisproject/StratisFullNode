using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Flurl;
using Flurl.Http;
using NBitcoin;
using Stratis.Bitcoin.Features.ColdStaking.Models;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.IntegrationTests.Common;
using Stratis.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Stratis.Bitcoin.IntegrationTests.Common.ReadyData;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Tests.Common;
using Xunit;

namespace Stratis.Bitcoin.IntegrationTests.Wallet
{
    public class OfflineSigningTests
    {
        private readonly Network network;

        public OfflineSigningTests()
        {
            this.network = new StraxRegTest();
        }

        [Fact]
        public async Task SignTransactionOffline()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode miningNode = builder.CreateStratisPosNode(this.network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();
                CoreNode onlineNode = builder.CreateStratisPosNode(this.network).Start();
                CoreNode offlineNode = builder.CreateStratisPosNode(this.network).WithWallet().Start();
                TestHelper.ConnectAndSync(miningNode, onlineNode);

                // Get the extpubkey from the offline node to restore on the online node.
                string extPubKey = await $"http://localhost:{offlineNode.ApiPort}/api"
                    .AppendPathSegment("wallet/extpubkey")
                    .SetQueryParams(new { walletName = "mywallet", accountName = "account 0" })
                    .GetJsonAsync<string>();

                // Load the extpubkey onto the online node.
                await $"http://localhost:{onlineNode.ApiPort}/api"
                    .AppendPathSegment("wallet/recover-via-extpubkey")
                    .PostJsonAsync(new WalletExtPubRecoveryRequest
                    {
                        Name = "mywallet",
                        AccountIndex = 0,
                        ExtPubKey = extPubKey,
                        CreationDate = DateTime.Today
                    })
                    .ReceiveJson();

                TestHelper.SendCoins(miningNode, onlineNode, Money.Coins(5.0m));
                TestHelper.MineBlocks(miningNode, 1);

                // Build the offline signing template from the online node. No password is needed.
                BuildOfflineSignResponse offlineTemplate = await $"http://localhost:{onlineNode.ApiPort}/api"
                    .AppendPathSegment("wallet/build-offline-sign-request")
                    .PostJsonAsync(new BuildTransactionRequest
                    {
                        WalletName = "mywallet",
                        AccountName = "account 0",
                        FeeAmount = "0.01",
                        ShuffleOutputs = true,
                        AllowUnconfirmed = true,
                        Recipients = new List<RecipientModel>() {
                            new RecipientModel
                            {
                                DestinationAddress = new Key().ScriptPubKey.GetDestinationAddress(this.network).ToString(),
                                Amount = "1"
                            }
                        }
                    })
                    .ReceiveJson<BuildOfflineSignResponse>();

                // Now build the actual transaction on the offline node. It is not synced with the others and only has the information
                // in the signing request and its own wallet to construct the transaction with.
                WalletBuildTransactionModel builtTransactionModel = await $"http://localhost:{offlineNode.ApiPort}/api"
                    .AppendPathSegment("wallet/offline-sign-request")
                    .PostJsonAsync(new OfflineSignRequest()
                    {
                        WalletName = offlineTemplate.WalletName,
                        WalletAccount = offlineTemplate.WalletAccount,
                        WalletPassword = "password",
                        UnsignedTransaction = offlineTemplate.UnsignedTransaction,
                        Fee = offlineTemplate.Fee,
                        Utxos = offlineTemplate.Utxos,
                        Addresses = offlineTemplate.Addresses
                    })
                    .ReceiveJson<WalletBuildTransactionModel>();

                // Send the signed transaction from the online node (doesn't really matter, could equally be from the mining node).
                await $"http://localhost:{onlineNode.ApiPort}/api"
                    .AppendPathSegment("wallet/send-transaction")
                    .PostJsonAsync(new SendTransactionRequest
                    {
                        Hex = builtTransactionModel.Hex
                    })
                    .ReceiveJson<WalletSendTransactionModel>();

                // Check that the transaction is valid and therefore relayed, and able to be mined into a block.
                TestBase.WaitLoop(() => miningNode.CreateRPCClient().GetRawMempool().Length == 1);
                TestHelper.MineBlocks(miningNode, 1);
                TestBase.WaitLoop(() => miningNode.CreateRPCClient().GetRawMempool().Length == 0);
            }
        }

        [Fact]
        public async Task SignColdStakingSetupOffline()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode miningNode = builder.CreateStratisColdStakingNode(this.network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();
                CoreNode onlineNode = builder.CreateStratisColdStakingNode(this.network).Start();
                CoreNode offlineNode = builder.CreateStratisColdStakingNode(this.network).WithWallet().Start();

                // The offline node never gets connected to anything.
                TestHelper.ConnectAndSync(miningNode, onlineNode);

                // Get the extpubkey from the offline node to restore on the online node.
                string extPubKey = await $"http://localhost:{offlineNode.ApiPort}/api"
                    .AppendPathSegment("wallet/extpubkey")
                    .SetQueryParams(new { walletName = "mywallet", accountName = "account 0" })
                    .GetJsonAsync<string>();

                // Load the extpubkey onto the online node.
                await $"http://localhost:{onlineNode.ApiPort}/api"
                    .AppendPathSegment("wallet/recover-via-extpubkey")
                    .PostJsonAsync(new WalletExtPubRecoveryRequest
                    {
                        Name = "coldwallet",
                        AccountIndex = 0,
                        ExtPubKey = extPubKey,
                        CreationDate = DateTime.Today - TimeSpan.FromDays(1)
                    })
                    .ReceiveJson();

                // Get a mnemonic for the hot wallet.
                string hotWalletMnemonic = await $"http://localhost:{onlineNode.ApiPort}/api"
                    .AppendPathSegment("wallet/mnemonic")
                    .GetJsonAsync<string>();

                // Restore the hot wallet on the online node.
                // This is needed because the hot address needs to have a private key available to stake with.
                await $"http://localhost:{onlineNode.ApiPort}/api"
                    .AppendPathSegment("wallet/recover")
                    .PostJsonAsync(new WalletRecoveryRequest()
                    {
                        Name = "hotwallet",
                        Mnemonic = hotWalletMnemonic,
                        Password = "password",
                        Passphrase = "",
                        CreationDate = DateTime.Today - TimeSpan.FromDays(1)
                    })
                    .ReceiveJson();

                // Get the hot address from the online node.
                CreateColdStakingAccountResponse hotAccount = await $"http://localhost:{onlineNode.ApiPort}/api"
                    .AppendPathSegment("coldstaking/cold-staking-account")
                    .PostJsonAsync(new CreateColdStakingAccountRequest()
                    {
                        WalletName = "hotwallet",
                        WalletPassword = "password",
                        IsColdWalletAccount = false
                    })
                    .ReceiveJson<CreateColdStakingAccountResponse>();

                string hotAddress = (await $"http://localhost:{onlineNode.ApiPort}/api"
                    .AppendPathSegment("coldstaking/cold-staking-address")
                    .SetQueryParams(new { walletName = "hotwallet", isColdWalletAddress = "false" })
                    .GetJsonAsync<GetColdStakingAddressResponse>()).Address;

                string coldWalletUnusedAddress = await $"http://localhost:{onlineNode.ApiPort}/api"
                    .AppendPathSegment("wallet/unusedaddress")
                    .SetQueryParams(new { walletName = "coldwallet", accountName = "account 0" })
                    .GetJsonAsync<string>();

                // Send some funds to the cold wallet's default (non-special) account to use for the staking setup.
                string fundTransaction = (await $"http://localhost:{miningNode.ApiPort}/api"
                    .AppendPathSegment("wallet/build-transaction")
                    .PostJsonAsync(new BuildTransactionRequest
                    {
                        WalletName = "mywallet",
                        Password = "password",
                        AccountName = "account 0",
                        FeeType = "high",
                        Recipients = new List<RecipientModel>() { new RecipientModel() { Amount = "5", DestinationAddress = coldWalletUnusedAddress } }
                    })
                    .ReceiveJson<WalletBuildTransactionModel>()).Hex;

                await $"http://localhost:{miningNode.ApiPort}/api"
                    .AppendPathSegment("wallet/send-transaction")
                    .PostJsonAsync(new SendTransactionRequest()
                    {
                        Hex = fundTransaction
                    })
                    .ReceiveJson<WalletSendTransactionModel>();

                TestBase.WaitLoop(() => miningNode.CreateRPCClient().GetRawMempool().Length > 0);
                TestHelper.MineBlocks(miningNode, 1);

                // Set up cold staking account on offline node to get the needed cold address.
                CreateColdStakingAccountResponse coldAccount = await $"http://localhost:{offlineNode.ApiPort}/api"
                    .AppendPathSegment("coldstaking/cold-staking-account")
                    .PostJsonAsync(new CreateColdStakingAccountRequest()
                    {
                        WalletName = "mywallet",
                        WalletPassword = "password",
                        IsColdWalletAccount = true
                    })
                    .ReceiveJson<CreateColdStakingAccountResponse>();

                string coldAddress = (await $"http://localhost:{offlineNode.ApiPort}/api"
                    .AppendPathSegment("coldstaking/cold-staking-address")
                    .SetQueryParams(new { walletName = "mywallet", isColdWalletAddress = "true" })
                    .GetJsonAsync<GetColdStakingAddressResponse>()).Address;

                // Build the offline cold staking template from the online node. No password is needed.
                BuildOfflineSignResponse offlineTemplate = await $"http://localhost:{onlineNode.ApiPort}/api"
                    .AppendPathSegment("coldstaking/setup-offline-cold-staking")
                    .PostJsonAsync(new SetupOfflineColdStakingRequest()
                    {
                        ColdWalletAddress = coldAddress,
                        HotWalletAddress = hotAddress,
                        WalletName = "coldwallet",
                        WalletAccount = "account 0",
                        Amount = "5", // Check that we can send the entire available balance in the setup
                        Fees = "0.01",
                        SubtractFeeFromAmount = true,
                        SegwitChangeAddress = false,
                        SplitCount = 10
                    })
                    .ReceiveJson<BuildOfflineSignResponse>();

                // Now build the actual transaction on the offline node. It is not synced with the others and only has the information
                // in the signing request and its own wallet to construct the transaction with.
                // Note that the wallet name and account name on the offline node may not actually match those from the online node.
                WalletBuildTransactionModel builtTransactionModel = await $"http://localhost:{offlineNode.ApiPort}/api"
                    .AppendPathSegment("wallet/offline-sign-request")
                    .PostJsonAsync(new OfflineSignRequest()
                    {
                        WalletName = "mywallet",
                        WalletAccount = offlineTemplate.WalletAccount,
                        WalletPassword = "password",
                        UnsignedTransaction = offlineTemplate.UnsignedTransaction,
                        Fee = offlineTemplate.Fee,
                        Utxos = offlineTemplate.Utxos,
                        Addresses = offlineTemplate.Addresses
                    })
                    .ReceiveJson<WalletBuildTransactionModel>();

                Dictionary<string, int> txCountBefore = await $"http://localhost:{onlineNode.ApiPort}/api"
                    .AppendPathSegment("wallet/transactionCount")
                    .SetQueryParams(new { walletName = "hotwallet", accountName = hotAccount.AccountName })
                    .GetJsonAsync<Dictionary<string, int>>();

                Assert.True(txCountBefore.Values.First() == 0);

                // Send the signed transaction from the online node (doesn't really matter, could equally be from the mining node).
                await $"http://localhost:{onlineNode.ApiPort}/api"
                    .AppendPathSegment("wallet/send-transaction")
                    .PostJsonAsync(new SendTransactionRequest
                    {
                        Hex = builtTransactionModel.Hex
                    })
                    .ReceiveJson<WalletSendTransactionModel>();

                // Check that the transaction is valid and therefore relayed, and able to be mined into a block.
                TestBase.WaitLoop(() => miningNode.CreateRPCClient().GetRawMempool().Length == 1);
                TestHelper.MineBlocks(miningNode, 1);
                TestBase.WaitLoop(() => miningNode.CreateRPCClient().GetRawMempool().Length == 0);

                Dictionary<string, int> txCountAfter = await $"http://localhost:{onlineNode.ApiPort}/api"
                    .AppendPathSegment("wallet/transactionCount")
                    .SetQueryParams(new { walletName = "hotwallet", accountName = hotAccount.AccountName })
                    .GetJsonAsync<Dictionary<string, int>>();

                Assert.True(txCountAfter.Values.First() > 0);
            }
        }
    }
}
