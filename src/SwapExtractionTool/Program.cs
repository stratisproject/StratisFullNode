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
            int startBlock = 0;
            Network straxNetwork;

            if (args.Contains("-testnet"))
            {
                startBlock = 1528858;

                stratisNetworkApiPort = 27103;
                straxNetwork = new StraxTest();
            }
            else
            {
                startBlock = 1987275;

                stratisNetworkApiPort = 17103;
                straxNetwork = new StraxMain();
            }

            var arg = args.FirstOrDefault(a => a.StartsWith("-startfrom"));
            if (arg != null)
                int.TryParse(arg.Split('=')[1], out startBlock);

            if (args.Contains("-swapvote"))
            {
                var service = new VoteExtractionService(stratisNetworkApiPort, straxNetwork);

                if (args.Contains("-swapvote"))
                    await service.RunAsync(VoteType.SwapVote, startBlock);
            }
        }
    }
}
