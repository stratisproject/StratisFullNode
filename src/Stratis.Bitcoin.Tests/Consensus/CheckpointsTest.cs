using System.Collections.Generic;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Settings;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Tests.Common;
using Xunit;

namespace Stratis.Bitcoin.Tests.Consensus
{
    public class CheckPointsTest
    {
        private readonly Network network;

        public CheckPointsTest()
        {
            this.network = KnownNetworks.Main;
        }

        [Fact]
        public void GetLastCheckPointHeight_WithoutConsensusSettings_ReturnsZero()
        {
            var checkpoints = new Checkpoints();

            int result = checkpoints.GetLastCheckpointHeight();

            Assert.Equal(0, result);
        }

        [Fact]
        public void GetLastCheckPointHeight_SettingsDisabledCheckpoints_DoesNotLoadCheckpoints()
        {
            var checkpoints = new Checkpoints(this.network, new ConsensusSettings(NodeSettings.Default(this.network)) { UseCheckpoints = false });

            int result = checkpoints.GetLastCheckpointHeight();

            Assert.Equal(0, result);
        }

        [Fact]
        public void GetLastCheckPointHeight_BitcoinMainnet_ReturnsLastCheckPointHeight()
        {
            var checkpoints = new Checkpoints(this.network, new ConsensusSettings(NodeSettings.Default(this.network)) { UseCheckpoints = true });

            int result = checkpoints.GetLastCheckpointHeight();

            Assert.Equal(610000, result);
        }

        [Fact]
        public void GetLastCheckPointHeight_BitcoinTestnet_ReturnsLastCheckPointHeight()
        {
            var checkpoints = new Checkpoints(KnownNetworks.TestNet, new ConsensusSettings(NodeSettings.Default(KnownNetworks.StraxTest)) { UseCheckpoints = true });

            int result = checkpoints.GetLastCheckpointHeight();

            Assert.Equal(1400000, result);
        }

        [Fact]
        public void GetLastCheckPointHeight_BitcoinRegTestNet_DoesNotLoadCheckpoints()
        {
            var checkpoints = new Checkpoints(KnownNetworks.RegTest, new ConsensusSettings(NodeSettings.Default(KnownNetworks.StraxTest)) { UseCheckpoints = true });

            int result = checkpoints.GetLastCheckpointHeight();

            Assert.Equal(0, result);
        }

        [Fact]
        public void GetLastCheckPointHeight_StraxMainnet_ReturnsLastCheckPointHeight()
        {
            var checkpoints = new Checkpoints(KnownNetworks.StraxMain, new ConsensusSettings(NodeSettings.Default(KnownNetworks.StraxTest)) { UseCheckpoints = true });

            int result = checkpoints.GetLastCheckpointHeight();

            Assert.Equal(150000, result);
        }

        [Fact]
        public void GetLastCheckPointHeight_StraxTestnet_ReturnsLastCheckPointHeight()
        {
            var checkpoints = new Checkpoints(KnownNetworks.StraxTest, new ConsensusSettings(NodeSettings.Default(KnownNetworks.StraxTest)) { UseCheckpoints = true });

            int result = checkpoints.GetLastCheckpointHeight();

            Assert.Equal(250_000, result);
        }

        [Fact]
        public void GetLastCheckPointHeight_StratisRegTestNet_DoesNotLoadCheckpoints()
        {
            var checkpoints = new Checkpoints(KnownNetworks.StraxRegTest, new ConsensusSettings(NodeSettings.Default(KnownNetworks.StraxTest)) { UseCheckpoints = true });

            int result = checkpoints.GetLastCheckpointHeight();

            Assert.Equal(0, result);
        }

        [Fact]
        public void GetLastCheckPointHeight_CheckpointsEnabledAfterLoad_RetrievesCheckpointsCorrectly()
        {
            var consensusSettings = new ConsensusSettings(NodeSettings.Default(this.network)) { UseCheckpoints = false };
            var checkpoints = new Checkpoints(this.network, consensusSettings);

            int result = checkpoints.GetLastCheckpointHeight();
            Assert.Equal(0, result);

            consensusSettings.UseCheckpoints = true;

            result = checkpoints.GetLastCheckpointHeight();
            Assert.Equal(610000, result);
        }

        [Fact]
        public void GetCheckPoint_WithoutConsensusSettings_ReturnsNull()
        {
            var checkpoints = new Checkpoints();

            CheckpointInfo result = checkpoints.GetCheckpoint(11111);

            Assert.Null(result);
        }

        [Fact]
        public void GetCheckPoint_CheckpointExists_PoWChain_ReturnsCheckpoint()
        {
            var checkpoints = new Checkpoints(this.network, new ConsensusSettings(NodeSettings.Default(this.network)) { UseCheckpoints = true });

            CheckpointInfo result = checkpoints.GetCheckpoint(11111);

            Assert.Equal(new uint256("0x0000000069e244f73d78e8fd29ba2fd2ed618bd6fa2ee92559f542fdb26e7c1d"), result.Hash);
            Assert.Null(result.StakeModifierV2);
        }

        [Fact]
        public void GetCheckPoint_CheckpointDoesNotExist_ReturnsNull()
        {
            var checkpoints = new Checkpoints(this.network, new ConsensusSettings(NodeSettings.Default(this.network)) { UseCheckpoints = true });

            CheckpointInfo result = checkpoints.GetCheckpoint(11112);

            Assert.Null(result);
        }

        [Fact]
        public void GetCheckPoint_CheckpointsEnabledAfterLoad_RetrievesCheckpointsCorrectly()
        {
            var consensusSettings = new ConsensusSettings(NodeSettings.Default(this.network)) { UseCheckpoints = false };
            var checkpoints = new Checkpoints(this.network, consensusSettings);

            CheckpointInfo result = checkpoints.GetCheckpoint(11112);
            Assert.Null(result);

            consensusSettings.UseCheckpoints = true;

            result = checkpoints.GetCheckpoint(11111);
            Assert.Equal(new uint256("0x0000000069e244f73d78e8fd29ba2fd2ed618bd6fa2ee92559f542fdb26e7c1d"), result.Hash);
            Assert.Null(result.StakeModifierV2);
        }

        [Fact]
        public void CheckHardened_CheckpointsEnabledAfterLoad_RetrievesCheckpointsCorrectly()
        {
            var consensusSettings = new ConsensusSettings(NodeSettings.Default(this.network)) { UseCheckpoints = false };
            var checkpoints = new Checkpoints(this.network, consensusSettings);

            bool result = checkpoints.CheckHardened(11111, new uint256("0x0000000059e244f73d78e8fd29ba2fd2ed618bd6fa2ee92559f542fdb26e7c1e")); // invalid hash
            Assert.True(result);

            consensusSettings.UseCheckpoints = true;

            result = checkpoints.CheckHardened(11111, new uint256("0x0000000059e244f73d78e8fd29ba2fd2ed618bd6fa2ee92559f542fdb26e7c1e")); // invalid hash
            Assert.False(result);
        }

        [Fact]
        public void CheckHardened_CheckpointExistsWithHashAtHeight_ReturnsTrue()
        {
            var checkpoints = new Checkpoints(this.network, new ConsensusSettings(NodeSettings.Default(this.network)) { UseCheckpoints = true });

            bool result = checkpoints.CheckHardened(11111, new uint256("0x0000000069e244f73d78e8fd29ba2fd2ed618bd6fa2ee92559f542fdb26e7c1d"));

            Assert.True(result);
        }

        [Fact]
        public void CheckHardened_CheckpointExistsWithDifferentHashAtHeight_ReturnsTrue()
        {
            var checkpoints = new Checkpoints(this.network, new ConsensusSettings(NodeSettings.Default(this.network)) { UseCheckpoints = true });

            bool result = checkpoints.CheckHardened(11111, new uint256("0x0000000059e244f73d78e8fd29ba2fd2ed618bd6fa2ee92559f542fdb26e7c1e"));

            Assert.False(result);
        }

        [Fact]
        public void CheckHardened_CheckpointDoesNotExistAtHeight_ReturnsTrue()
        {
            var checkpoints = new Checkpoints(this.network, new ConsensusSettings(NodeSettings.Default(this.network)) { UseCheckpoints = true });

            bool result = checkpoints.CheckHardened(11112, new uint256("0x7d61c139a471821caa6b7635a4636e90afcfe5e195040aecbc1ad7d24924db1e"));

            Assert.True(result);
        }

        [Fact]
        public void CheckHardened_WithoutConsensusSettings_ReturnsTrue()
        {
            var checkpoints = new Checkpoints();

            bool result = checkpoints.CheckHardened(11111, new uint256("0x0000000069e244f73d78e8fd29ba2fd2ed618bd6fa2ee92559f542fdb26e7c1d"));

            Assert.True(result);
        }

        [Fact]
        public void VerifyCheckpoints_BitcoinMainnet()
        {
            var verifyableCheckpoints = new Dictionary<int, CheckpointInfo>
            {
                { 11111, new CheckpointInfo(new uint256("0x0000000069e244f73d78e8fd29ba2fd2ed618bd6fa2ee92559f542fdb26e7c1d")) },
                { 33333, new CheckpointInfo(new uint256("0x000000002dd5588a74784eaa7ab0507a18ad16a236e7b1ce69f00d7ddfb5d0a6")) },
                { 74000, new CheckpointInfo(new uint256("0x0000000000573993a3c9e41ce34471c079dcf5f52a0e824a81e7f953b8661a20")) },
                { 105000, new CheckpointInfo(new uint256("0x00000000000291ce28027faea320c8d2b054b2e0fe44a773f3eefb151d6bdc97")) },
                { 134444, new CheckpointInfo(new uint256("0x00000000000005b12ffd4cd315cd34ffd4a594f430ac814c91184a0d42d2b0fe")) },
                { 168000, new CheckpointInfo(new uint256("0x000000000000099e61ea72015e79632f216fe6cb33d7899acb35b75c8303b763")) },
                { 193000, new CheckpointInfo(new uint256("0x000000000000059f452a5f7340de6682a977387c17010ff6e6c3bd83ca8b1317")) },
                { 210000, new CheckpointInfo(new uint256("0x000000000000048b95347e83192f69cf0366076336c639f9b7228e9ba171342e")) },
                { 216116, new CheckpointInfo(new uint256("0x00000000000001b4f4b433e81ee46494af945cf96014816a4e2370f11b23df4e")) },
                { 225430, new CheckpointInfo(new uint256("0x00000000000001c108384350f74090433e7fcf79a606b8e797f065b130575932")) },
                { 250000, new CheckpointInfo(new uint256("0x000000000000003887df1f29024b06fc2200b55f8af8f35453d7be294df2d214")) },
                { 279000, new CheckpointInfo(new uint256("0x0000000000000001ae8c72a0b0c301f67e3afca10e819efa9041e458e9bd7e40")) },
                { 295000, new CheckpointInfo(new uint256("0x00000000000000004d9b4ef50f0f9d686fd69db2e03af35a100370c64632a983")) },
                { 486000, new CheckpointInfo(new uint256("0x000000000000000000a2a8104d61651f76c666b70754d6e9346176385f7afa24")) },
                { 491800, new CheckpointInfo(new uint256("0x000000000000000000d80de1f855902b50941bc3a3d0f71064d9613fd3943dc4")) },
            };

            var checkpoints = new Checkpoints(this.network, new ConsensusSettings(NodeSettings.Default(this.network)) { UseCheckpoints = true });

            VerifyCheckpoints(checkpoints, verifyableCheckpoints);
        }

        [Fact]
        public void VerifyCheckpoints_BitcoinTestnet()
        {
            var verifyableCheckpoints = new Dictionary<int, CheckpointInfo>
            {
                { 546, new CheckpointInfo(new uint256("000000002a936ca763904c3c35fce2f3556c559c0214345d31b1bcebf76acb70")) },
                { 1210000, new CheckpointInfo(new uint256("00000000461201277cf8c635fc10d042d6f0a7eaa57f6c9e8c099b9e0dbc46dc")) },
            };

            var checkpoints = new Checkpoints(KnownNetworks.TestNet, new ConsensusSettings(NodeSettings.Default(this.network)) { UseCheckpoints = true });

            VerifyCheckpoints(checkpoints, verifyableCheckpoints);
        }

        private void VerifyCheckpoints(Checkpoints checkpoints, Dictionary<int, CheckpointInfo> checkpointValues)
        {
            foreach (KeyValuePair<int, CheckpointInfo> checkpoint in checkpointValues)
            {
                CheckpointInfo result = checkpoints.GetCheckpoint(checkpoint.Key);

                Assert.Equal(checkpoint.Value.Hash, result.Hash);
                Assert.Equal(checkpoint.Value.StakeModifierV2, result.StakeModifierV2);
            }
        }
    }
}
