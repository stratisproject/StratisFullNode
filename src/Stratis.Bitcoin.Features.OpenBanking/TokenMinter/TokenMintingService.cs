using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.OpenBanking.OpenBanking;
using Stratis.Bitcoin.Features.SmartContracts.MetadataTracker;
using Stratis.Bitcoin.Features.Wallet.Interfaces;

namespace Stratis.Bitcoin.Features.OpenBanking.TokenMinter
{
    /// <summary>Mints tokens for deposits discovered by the OpenBankingAPI.</summary>
    public class TokenMintingService : ITokenMintingService
    {
        private readonly IOpenBankingService openBankingAPI;
        private readonly ITokenMintingTransactionBuilder tokenMintingTransactionBuilder;
        private readonly IBroadcasterManager broadcasterManager;
        private readonly Dictionary<MetadataTrackerEnum, IOpenBankAccount> registeredAccounts;
        private readonly ILogger logger;

        public TokenMintingService(ITokenMintingTransactionBuilder tokenMintingTransactionBuilder, IOpenBankingService openBankingAPI, IBroadcasterManager broadcasterManager, DataFolder dataFolder, ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            this.openBankingAPI = openBankingAPI;
            this.tokenMintingTransactionBuilder = tokenMintingTransactionBuilder;
            this.broadcasterManager = broadcasterManager;
            this.registeredAccounts = new Dictionary<MetadataTrackerEnum, IOpenBankAccount>();
            this.ReadConfig(dataFolder.RootPath);
        }

        private void ReadConfig(string rootPath)
        {
            try
            {
                this.registeredAccounts.Clear();

                string fileName = Path.Combine(rootPath, "minter.conf");

                if (!File.Exists(fileName))
                    return;

                string json = File.ReadAllText(fileName);

                foreach (var definition in JsonSerializer.Deserialize<OpenBankAccount[]>(json))
                {
                    this.Register(definition);
                }
            }
            catch (Exception err)
            {
               this.logger.LogError(err.Message);
            }
        }

        public void Initialize()
        {
        }

        public void Register(IOpenBankAccount openBankAccount)
        {
            this.registeredAccounts[openBankAccount.MetaDataTrackerEnum] = openBankAccount;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            foreach (IOpenBankAccount openBankAccount in this.registeredAccounts.Values)
            {
                this.openBankingAPI.UpdateDeposits(openBankAccount);
                this.openBankingAPI.UpdateDepositStatus(openBankAccount);

                // Look for deposits in the OpenBankingAPI that have a status of 'D'.
                foreach (var deposit in this.openBankingAPI.GetOpenBankDeposits(openBankAccount, OpenBankDepositState.Detected).ToArray())
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    Transaction transaction = this.tokenMintingTransactionBuilder.BuildSignedTransaction(openBankAccount, deposit);

                    this.openBankingAPI.SetTransactionId(openBankAccount, deposit, transaction.GetHash());

                    // Add to memory pool.
                    await this.broadcasterManager.BroadcastTransactionAsync(transaction);

                    // Leave it to the OpenBankAPI to update the deposit state.
                }
            }
        }
    }
}
