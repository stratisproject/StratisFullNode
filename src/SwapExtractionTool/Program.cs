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
            int endBlock = 0;
            string apiUrl = null;

            if (args.Contains("-testnet"))
            {
                startBlock = 1528858;
                endBlock = -1;

                stratisNetworkApiPort = 27103;

                apiUrl = "http://localhost";
            }
            else
            {
                startBlock = 1987275;
                endBlock = -1;

                stratisNetworkApiPort = 17103;

                apiUrl = "http://localhost";
            }

            var arg = args.FirstOrDefault(a => a.StartsWith("-startfrom"));
            if (arg != null)
                int.TryParse(arg.Split('=')[1], out startBlock);

            arg = args.FirstOrDefault(a => a.StartsWith("-apiuri"));
            if (arg != null)
                apiUrl = arg.Split('=')[1];

            arg = args.FirstOrDefault(a => a.StartsWith("-apiport"));
            if (arg != null)
                int.TryParse(arg.Split('=')[1], out stratisNetworkApiPort);

            arg = args.FirstOrDefault(a => a.StartsWith("-startblock"));
            if (arg != null)
                int.TryParse(arg.Split('=')[1], out startBlock);

            arg = args.FirstOrDefault(a => a.StartsWith("-endblock"));
            if (arg != null)
                int.TryParse(arg.Split('=')[1], out endBlock);

            if (args.Contains("-swapvote"))
            {
                var service = new VoteExtractionService(apiUrl, stratisNetworkApiPort);

                if (args.Contains("-swapvote"))
                    await service.RunAsync(VoteType.SwapVote, startBlock, endBlock);
            }
        }
    }
}
