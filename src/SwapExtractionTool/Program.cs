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
            Network stratisNetwork;
            Network straxNetwork;
            if (args.Contains("-testnet"))
            {
                stratisNetwork = new StratisTest();
                straxNetwork = new StraxTest();
            }
            else
            {
                stratisNetwork = new StratisMain();
                straxNetwork = new StraxMain();
            }

            var service = new SwapExtractionService(stratisNetwork, straxNetwork);
            await service.RunAsync();
        }
    }
}
