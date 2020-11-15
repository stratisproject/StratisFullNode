using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.PoA
{
    public class PoAMinerSettings
    {
        /// <summary>Allows mining in case node is in IBD and not connected to anyone.</summary>
        public bool BootstrappingMode { get; private set; }

        /// <summary>
        /// An address to use when mining, if not specified an address from the wallet will be used.
        /// </summary>
        public string MineAddress { get; set; }

        public PoAMinerSettings(NodeSettings nodeSettings)
        {
            Guard.NotNull(nodeSettings, nameof(nodeSettings));

            TextFileConfiguration config = nodeSettings.ConfigReader;

            this.BootstrappingMode = config.GetOrDefault<bool>("bootstrap", false);
            this.MineAddress = config.GetOrDefault<string>("mineaddress", null);
        }

        public void DisableBootstrap()
        {
            this.BootstrappingMode = false;
        }
    }
}
