using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

namespace Stratis.External.MasternodeRegistration
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var service = new RegistrationService();

            NetworkType networkType = NetworkType.Mainnet;

            if (args.Contains("-testnet"))
                networkType = NetworkType.Testnet;

            await service.StartAsync(networkType);
        }
    }
}
