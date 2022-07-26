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
using Stratis.Bitcoin.Interfaces;

namespace Stratis.Bitcoin.Features.OpenBanking.TokenMinter
{
    /// <summary>Mints tokens for deposits discovered by the OpenBankingAPI.</summary>
    public class TokenMintingService : ITokenMintingService
    {
        private readonly IOpenBankingService openBankingService;
        private readonly ITokenMintingTransactionBuilder tokenMintingTransactionBuilder;
        private readonly IBroadcasterManager broadcasterManager;
        private readonly IInitialBlockDownloadState initialBlockDownloadState;

        private readonly Dictionary<MetadataTrackerEnum, IOpenBankAccount> registeredAccounts;
        private readonly ILogger logger;

        public TokenMintingService(ITokenMintingTransactionBuilder tokenMintingTransactionBuilder, IOpenBankingService openBankingAPI, IBroadcasterManager broadcasterManager, DataFolder dataFolder, ILoggerFactory loggerFactory, IInitialBlockDownloadState initialBlockDownloadState)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.openBankingService = openBankingAPI;
            this.tokenMintingTransactionBuilder = tokenMintingTransactionBuilder;
            this.broadcasterManager = broadcasterManager;
            this.initialBlockDownloadState = initialBlockDownloadState;

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
            if (this.initialBlockDownloadState.IsInitialBlockDownload())
                return;

            foreach (IOpenBankAccount openBankAccount in this.registeredAccounts.Values)
            {
                this.openBankingService.UpdateDeposits(openBankAccount);
                this.openBankingService.UpdateDepositStatus(openBankAccount);

                // Look for deposits in the OpenBankingAPI that have a status of booked.
                foreach (var deposit in this.openBankingService.GetOpenBankDeposits(openBankAccount, OpenBankDepositState.Booked).ToArray())
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    if (deposit.ValueDateTimeUTC > DateTime.UtcNow)
                        continue;

                    Transaction transaction = this.tokenMintingTransactionBuilder.BuildSignedTransaction(openBankAccount, deposit);

                    this.openBankingService.SetTransactionId(openBankAccount, deposit, transaction.GetHash());

                    // Add to memory pool.
                    await this.broadcasterManager.BroadcastTransactionAsync(transaction);

                    // Leave it to the OpenBankAPI to update the deposit state.
                }
            }
        }
    }
}
