using System.Threading.Tasks;
using NBitcoin;

namespace SwapExtractionTool
{
    public sealed class ExtractionTool
    {
        private readonly int stratisNetworkApiPort;
        private readonly Network straxNetwork;

        public ExtractionTool(int stratisNetworkApiPort, Network straxNetwork)
        {
            this.stratisNetworkApiPort = stratisNetworkApiPort;
            this.straxNetwork = straxNetwork;
        }

        public async Task RunAsync(ExtractionType extractionType, int startBlock, bool distribute = false)
        {
            if (extractionType == ExtractionType.Swap)
            {
                var service = new SwapExtractionService(this.stratisNetworkApiPort, this.straxNetwork);
                await service.RunAsync(startBlock, distribute);
            }

            if (extractionType == ExtractionType.Vote)
            {
                var service = new VoteExtractionService(this.stratisNetworkApiPort, this.straxNetwork);
                await service.RunAsync(startBlock);
            }
        }
    }
}
