using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Flurl;
using Flurl.Http;
using NBitcoin;
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
    }
}
