﻿using Newtonsoft.Json;

namespace Stratis.Bitcoin.Base.Deployments.Models
{
    /// <summary>
    /// Class representing information about a current locked in or active deployment.
    /// </summary>
    public class ThresholdActivationModel
    {
        /// <summary>
        /// BIP9 deployment name.
        /// </summary>
        [JsonProperty(PropertyName = "deploymentName")]
        public string DeploymentName { get; set; }

        /// <summary>
        /// BIP9 deployment index.
        /// </summary>
        [JsonProperty(PropertyName = "deploymentIndex")]
        public int DeploymentIndex { get; set; }

        /// <summary>
        /// Activation height.
        /// </summary>
        [JsonProperty(PropertyName = "activationHeight")]
        public int ActivationHeight { get; set; }

        /// <summary>
        /// Number of blocks with flags set that led to the deployment being locked in.
        /// </summary>
        [JsonProperty(PropertyName = "votes")]
        public int Votes { get; set; }

        /// <summary>
        /// The height at which the deployment was locked-in.
        /// </summary>
        [JsonProperty(PropertyName = "lockedInHeight")]
        public int? LockedInHeight { get; set; }

        /// <summary>
        /// The timestamp of the blocked at the "lockedInHeight".
        /// </summary>
        [JsonProperty(PropertyName = "lockedInTimestamp")]
        public long? LockedInTimestamp { get; set; }
    }
}