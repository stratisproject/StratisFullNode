using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stratis.Bitcoin.Controllers;
using Stratis.Bitcoin.Features.Wallet.Models;

namespace Stratis.Bitcoin.Features.Wallet.Controllers
{
    /// <summary>Rest client for <see cref="WalletController"/>.</summary>
    public interface IWalletClient : IRestApiClientBase
    {
        /// <summary><see cref="WalletController.SignMessageAsync"/></summary>
        Task<string> SignMessageAsync(SignMessageRequest request, CancellationToken cancellation = default);
    }

    /// <inheritdoc cref="IWalletClient"/>
    public class WalletClient : RestApiClientBase, IWalletClient
    {
        /// <summary>
        /// Currently the <paramref name="url"/> is required as it needs to be configurable for testing.
        /// <para>
        /// In a production/live scenario the sidechain and mainnet federation nodes should run on the same machine.
        /// </para>
        /// </summary>
        public WalletClient(ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory, string url, int port)
            : base(httpClientFactory, port, "Wallet", url)
        {
        }

        /// <inheritdoc />
        public Task<string> SignMessageAsync(SignMessageRequest request, CancellationToken cancellation = default)
        {
            return this.SendPostRequestAsync<SignMessageRequest, string>(request, "signmessage", cancellation);
        }
    }
}