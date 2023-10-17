using System.Threading.Tasks;
using Flurl;
using Flurl.Http;
using NBitcoin;
using Stratis.Bitcoin.Features.BlockStore.Models;

namespace SwapExtractionTool
{
    public abstract class ExtractionBase
    {
        public readonly string StratisNetworkApiUrl;
        public readonly int StratisNetworkApiPort;
        public readonly Network StraxNetwork;

        protected ExtractionBase(string apiUrl, int stratisNetworkApiPort, Network straxNetwork)
        {
            this.StratisNetworkApiUrl = apiUrl;
            this.StratisNetworkApiPort = stratisNetworkApiPort;
            this.StraxNetwork = straxNetwork;
        }

        protected async Task<BlockTransactionDetailsModel> RetrieveBlockAtHeightAsync(int blockHeight)
        {
            var blockHash = await $"{this.StratisNetworkApiUrl}:{this.StratisNetworkApiPort}/api"
                .AppendPathSegment("consensus/getblockhash")
                .SetQueryParams(new { height = blockHeight })
                .GetJsonAsync<string>();

            if (blockHash == null)
                return null;

            BlockTransactionDetailsModel blockModel = await $"{this.StratisNetworkApiUrl}:{this.StratisNetworkApiPort}/api"
                .AppendPathSegment("blockstore/block")
                .SetQueryParams(new SearchByHashRequest() { Hash = blockHash, ShowTransactionDetails = true, OutputJson = true })
                .GetJsonAsync<BlockTransactionDetailsModel>();

            return blockModel;
        }
    }
}
