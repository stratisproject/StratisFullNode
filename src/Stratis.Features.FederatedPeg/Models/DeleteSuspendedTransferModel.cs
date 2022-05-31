using Newtonsoft.Json;

namespace Stratis.Features.FederatedPeg.Models
{
    /// <summary>
    /// Model object to use to specify the suspended deposit id to delete.
    /// </summary>
    public sealed class DeleteSuspendedTransferModel
    {
        [JsonProperty(PropertyName = "depositid")]
        public string DepositId { get; set; }
    }
}
