using System.Threading;
using System.Threading.Tasks;
using Stratis.Bitcoin.Features.OpenBanking.OpenBanking;

namespace Stratis.Bitcoin.Features.OpenBanking.TokenMinter
{
    public interface ITokenMintingService
    {
        void Register(IOpenBankAccount openBankAccount);

        void Initialize();

        Task RunAsync(CancellationToken cancellationToken);
    }
}
