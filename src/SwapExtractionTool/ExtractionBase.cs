using System.Threading.Tasks;
using Flurl;
using Flurl.Http;
using NBitcoin;
using Newtonsoft.Json;
using Stratis.Bitcoin.Features.BlockStore.Models;

namespace SwapExtractionTool
{
    public class ConsensusTipModel
    {
        [JsonProperty(PropertyName = "tipHash")]
        public string TipHash { get; set; }

        [JsonProperty(PropertyName = "tipHeight")]
        public int TipHeight { get; set; }
    }

    public abstract class ExtractionBase
    {
        public readonly string StratisNetworkApiUrl;
        public readonly int StratisNetworkApiPort;
        
        protected ExtractionBase(string apiUrl, int stratisNetworkApiPort)
        {
            this.StratisNetworkApiUrl = apiUrl;
            this.StratisNetworkApiPort = stratisNetworkApiPort;
        }

        protected async Task<int> RetrieveBlockHeightAsync()
        {
            var consensusTip = await $"{this.StratisNetworkApiUrl}:{this.StratisNetworkApiPort}/api"
                .AppendPathSegment("consensus/tip")
                .GetJsonAsync<ConsensusTipModel>();

            if (consensusTip == null)
                return -1;

            return consensusTip.TipHeight;
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
