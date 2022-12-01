using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Settings;

namespace Stratis.Bitcoin.Features.Miner
{
    /// <summary>
    /// Configuration related to the miner interface.
    /// </summary>
    public class MinerSettings : BaseSettings
    {
        private const ulong MinimumSplitCoinValueDefaultValue = 100 * Money.COIN;

        private const ulong MinimumStakingCoinValueDefaultValue = 10 * Money.CENT;

        /// <summary>
        /// Enable the node to stake.
        /// </summary>
        [CommandLineOption("stake", "Enable POS.")]
        public bool Stake { get; private set; } = false;

        /// <summary>
        /// Enable splitting coins when staking.
        /// </summary>
        [CommandLineOption("enablecoinstakesplitting", "Enable splitting coins when staking.")]
        public bool EnableCoinStakeSplitting { get; private set; } = true;

        /// <summary>
        /// Minimum value a coin has to be in order to be considered for staking.
        /// </summary>
        /// <remarks>
        /// This can be used to save on CPU consumption by excluding small coins that would not significantly impact a wallet's staking power.
        /// </remarks>
        [CommandLineOption("minimumstakingcoinvalue", "Minimum size of the coins considered for staking, in satoshis.")]
        public ulong MinimumStakingCoinValue { get { return this.minimumStakingCoinValue; } private set { this.minimumStakingCoinValue = (value == 0) ? 1 : value; } }
        private ulong minimumStakingCoinValue = MinimumStakingCoinValueDefaultValue;

        /// <summary>
        /// Targeted minimum value of staking coins after splitting.
        /// </summary>
        [CommandLineOption("minimumsplitcoinvalue", "Targeted minimum value of staking coins after splitting, in satoshis.")]
        public ulong MinimumSplitCoinValue { get; private set; } = MinimumSplitCoinValueDefaultValue;

        /// <summary>
        /// Enable the node to mine.
        /// </summary>
        [CommandLineOption("mine", "Enable POW mining.")]

        public bool Mine { get; private set; } = false;

        /// <summary>
        /// An address to use when mining, if not specified an address from the wallet will be used.
        /// </summary>
        [CommandLineOption("mineaddress", "The address to use for mining (empty string to select an address from the wallet).")]
        public string MineAddress { get; set; } = null;

        /// <summary>
        /// The wallet password needed when staking to sign blocks.
        /// </summary>
        [CommandLineOption("walletpassword", "Password to unlock the wallet.", false)]
        public string WalletPassword { get; set; } = null;

        /// <summary>
        /// The wallet name to select outputs to stake.
        /// </summary>
        [CommandLineOption("walletname", "The wallet name to use when staking.", false)]
        public string WalletName { get; set; } = null;

        [CommandLineOption("blockmaxsize", "Maximum block size (in bytes) for the miner to generate.")]
        private uint BlockMaxSize { get { return this.blockMaxSize ?? this.nodeSettings.Network.Consensus.Options.MaxBlockSerializedSize; } set { this.blockMaxSize = value; } }
        private uint? blockMaxSize = null;

        [CommandLineOption("blockmaxweight", "Maximum block weight (in weight units) for the miner to generate.")]
        private uint BlockMaxWeight { get { return this.blockMaxWeight ?? this.nodeSettings.Network.Consensus.Options.MaxBlockWeight; } set { this.blockMaxWeight = value; } }
        private uint? blockMaxWeight = null;

        [CommandLineOption("blockmintxfee", "Set lowest fee rate (in BTC/kvB) for transactions to be included in block creation.")]
        private uint BlockMinTxFee { get { return this.blockMinTxFee ?? PowMining.DefaultBlockMinTxFee; } set { this.blockMaxWeight = value; } }
        private uint? blockMinTxFee = null;
        
        /// <summary>
        /// Settings for <see cref="BlockDefinition"/>.
        /// </summary>
        public BlockDefinitionOptions BlockDefinitionOptions { get; }

        /// <summary>
        /// Initializes an instance of the object from the node configuration.
        /// </summary>
        /// <param name="nodeSettings">The node configuration.</param>
        public MinerSettings(NodeSettings nodeSettings) : base(nodeSettings)
        {
            this.BlockDefinitionOptions = new BlockDefinitionOptions(this.BlockMaxWeight, this.BlockMaxSize, this.BlockMinTxFee).RestrictForNetwork(nodeSettings.Network);

            if (!this.Mine)
                this.MineAddress = null;

            if (!this.Stake)
            {
                this.WalletName = null;
                this.WalletPassword = null;
            }
        }
    }
}
