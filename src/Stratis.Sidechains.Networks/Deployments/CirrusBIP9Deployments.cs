using NBitcoin;

namespace Stratis.Sidechains.Networks.Deployments
{
    /// <summary>
    /// BIP9 deployments for the Cirrus network.
    /// </summary>
    public class CirrusBIP9Deployments : BIP9DeploymentsArray
    {
        // The position of each deployment in the deployments array. Note that this is decoupled from the actual position of the flag bit for the deployment in the block version.
        public const int Release1320 = 0;
        public const int Release1324 = 1;

        public const int FlagBitRelease1320 = 1;
        public const int FlagBitRelease1324 = 2;

        // The number of deployments.
        public const int NumberOfDeployments = 2;

        /// <summary>
        /// Constructs the BIP9 deployments array.
        /// </summary>
        public CirrusBIP9Deployments() : base(NumberOfDeployments)
        {
        }

        /// <summary>
        /// Gets the deployment flags to set when the deployment activates.
        /// </summary>
        /// <param name="deployment">The deployment number.</param>
        /// <returns>The deployment flags.</returns>
        public override BIP9DeploymentFlags GetFlags(int deployment)
        {
            // The flags get combined in the caller, so it is ok to make a fresh object here.
            var flags = new BIP9DeploymentFlags();

            return flags;
        }
    }
}
