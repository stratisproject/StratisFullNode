using System.Threading;
using System.Threading.Tasks;
using Stratis.Bitcoin.Features.OpenBanking.OpenBanking;

namespace Stratis.Bitcoin.Features.OpenBanking.TokenMinter
{
    /// <summary>
    /// Runs the stable coin minting service.
    /// </summary>
    public interface ITokenMintingService
    {
        /// <summary>
        /// Register information about a stablecoin to mint and a bank account for providing the related deposits.
        /// </summary>
        /// <param name="openBankAccount">Information about the bank account and stable coin to mint.</param>
        void Register(IOpenBankAccount openBankAccount);

        /// <summary>
        /// Initializes the service.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Called periodically by <see cref="OpenBankingFeature.RunMintingService"/> to mint stablecoins.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The asynchronous task.</returns>
        Task RunAsync(CancellationToken cancellationToken);
    }
}
