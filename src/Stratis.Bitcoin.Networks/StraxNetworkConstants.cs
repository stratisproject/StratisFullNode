namespace Stratis.Bitcoin.Networks
{
    public static class StraxNetworkConstants
    {
        /// <summary> Stratis maximal value for the calculated time offset. If the value is over this limit, the time syncing feature will be switched off. </summary>
        public const int StratisMaxTimeOffsetSeconds = 25 * 60;

        /// <summary> Stratis default value for the maximum tip age in seconds to consider the node in initial block download (2 hours). </summary>
        public const int StratisDefaultMaxTipAgeInSeconds = 2 * 60 * 60;

        /// <summary> The name of the root folder containing the different Stratis blockchains (StratisMain, StratisTest, StratisRegTest). </summary>
        public const string StraxRootFolderName = "strax";

        /// <summary> The default name used for the Strax configuration file. </summary>
        public const string StraxDefaultConfigFilename = "strax.conf";
    }
}
