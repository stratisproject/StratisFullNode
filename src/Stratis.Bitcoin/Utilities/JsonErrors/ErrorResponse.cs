using System.Collections.Generic;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Utilities.JsonErrors
{
    public class ErrorResponse
    {
        /// <summary>
        /// List of errors
        /// </summary>
        [JsonProperty(PropertyName = "errors")]
        public List<ErrorModel> Errors { get; set; }
    }

    public class ErrorModel
    {
        /// <summary>
        /// HTTP status code
        /// </summary>
        [JsonProperty(PropertyName = "status")]
        public int Status { get; set; }

        /// <summary>
        /// Basic error message
        /// </summary>
        [JsonProperty(PropertyName = "message")]
        public string Message { get; set; }

        /// <summary>
        /// Detailed error description
        /// </summary>
        [JsonProperty(PropertyName = "description")]
        public string Description { get; set; }
    }
}
