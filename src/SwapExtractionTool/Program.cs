using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using Stratis.Bitcoin.Networks;

namespace SwapExtractionTool
{
    class Program
    {
        static async Task Main(string[] args)
        {
            int stratisNetworkApiPort;
            Network straxNetwork;
            if (args.Contains("-testnet"))
            {
                stratisNetworkApiPort = 38221;
                straxNetwork = new StraxTest();
            }
            else
            {
                stratisNetworkApiPort = 37221;
                straxNetwork = new StraxMain();
            }

            var service = new SwapExtractionService(stratisNetworkApiPort, straxNetwork);
            await service.RunAsync(ExtractionType.Vote);
        }
    }
}
