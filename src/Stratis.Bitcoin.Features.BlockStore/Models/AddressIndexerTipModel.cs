using NBitcoin;
using Newtonsoft.Json;
using Stratis.Bitcoin.Utilities.JsonConverters;

namespace Stratis.Bitcoin.Features.BlockStore.Models
{
    public sealed class AddressIndexerTipModel
    {
        [JsonConverter(typeof(UInt256JsonConverter))]
        public uint256 TipHash { get; set; }

        public int? TipHeight { get; set; }
    }
}