using Newtonsoft.Json;
using Stratis.Bitcoin.Controllers.Converters;

namespace Stratis.Bitcoin.Controllers.Models
{
    /// <summary>
    /// Temporary workaround; the RPC middleware does not correctly return JSON for a raw string object, so we have to wrap a string response with this model.
    /// <seealso cref="HexModel">See also how 'getblockheader' returns a hex string.</seealso>
    /// </summary>
    [JsonConverter(typeof(ToStringJsonConverter))]
    public class StringModel
    {
        public string Str { get; set; }

        public StringModel(string str)
        {
            this.Str = str;
        }

        public override string ToString()
        {
            return this.Str;
        }
    }
}
