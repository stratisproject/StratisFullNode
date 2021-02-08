using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using Stratis.Bitcoin.Features.Miner.Staking;

namespace Stratis.Bitcoin.Features.Miner.Models
{
    /// <summary>
    /// Base model for requests.
    /// </summary>
    public class RequestModel
    {
        /// <summary>
        /// Creates a JSON serialized object.
        /// </summary>
        /// <returns>A JSON serialized object.</returns>
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
    }
    
    /// <summary>
    /// Model for the "startstaking" request.
    /// </summary>
    public class StartStakingRequest : RequestModel
    {
        /// <summary>
        /// The wallet password.
        /// </summary>
        [Required(ErrorMessage = "A password is required.")]
        public string Password { get; set; }

        /// <summary>
        /// The wallet name.
        /// </summary>
        [Required(ErrorMessage = "The name of the wallet is missing.")]
        public string Name { get; set; }
    }

    /// <summary>
    /// Model for the "startmultistaking" request.
    /// </summary>
    public class StartMultiStakingRequest : RequestModel
    {
        /// <summary>
        /// The list of one or more wallet credentials to start staking with.
        /// </summary>
        [Required(ErrorMessage = "The list of wallet credentials is required.")]
        public List<WalletSecret> WalletCredentials { get; set; }
    }

    /// <summary>
    /// Model for the "generate" mining request.
    /// </summary>
    public class MiningRequest : RequestModel
    {
        /// <summary>
        /// Number of blocks to mine.
        /// </summary>
        [Required(ErrorMessage = "The number of blocks to mine required.")]
        public int BlockCount { get; set; }
    }
}
