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
            Network network;
            if (args.Contains("-testnet"))
                network = new StratisTest();
            else
                network = new StratisMain();

            var service = new SwapExtractionService(network);
            await service.RunAsync();
        }
    }
}
