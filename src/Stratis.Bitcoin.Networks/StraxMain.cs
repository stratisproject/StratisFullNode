using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;
using NBitcoin.BouncyCastle.Math;
using NBitcoin.DataEncoders;
using NBitcoin.Protocol;
using Stratis.Bitcoin.Networks.Deployments;
using Stratis.Bitcoin.Networks.Policies;

namespace Stratis.Bitcoin.Networks
{
    public class StraxMain : Network
    {
        public StraxMain()
        {
            this.Name = "StraxMain";
            this.NetworkType = NetworkType.Mainnet;
            this.Magic = BitConverter.ToUInt32(Encoding.ASCII.GetBytes("StrX"));
            this.DefaultPort = 17105;
            this.DefaultMaxOutboundConnections = 16;
            this.DefaultMaxInboundConnections = 100;
            this.DefaultRPCPort = 17104;
            this.DefaultAPIPort = 17103;
            this.DefaultSignalRPort = 17102;
            this.MaxTipAge = 2 * 60 * 60;
            this.MinTxFee = 10000;
            this.FallbackFee = 10000;
            this.MinRelayTxFee = 10000;
            this.RootFolderName = StraxNetwork.StraxRootFolderName;
            this.DefaultConfigFilename = StraxNetwork.StraxDefaultConfigFilename;
            this.MaxTimeOffsetSeconds = 25 * 60;
            this.CoinTicker = "STRAX";
            this.DefaultBanTimeSeconds = 11250; // 500 (MaxReorg) * 45 (TargetSpacing) / 2 = 3 hours, 7 minutes and 30 seconds

            this.CirrusRewardDummyAddress = "CPqxvnzfXngDi75xBJKqi4e6YrFsinrJka"; // Cirrus main address

            // To successfully process the OP_FEDERATION opcode the federations should be known.
            this.Federations = new Federations();
            this.Federations.RegisterFederation(new Federation(new[]
            {
                new PubKey("03f5de5176e29e1e7d518ae76c1e020b1da18b57a3713ac81b16015026e232748e"),
                new PubKey("021043aacac5c8805e3bc62eb40e8d3c04070c56b21032d4bb14200ed6e4facf93"),
                new PubKey("0323033679aa439a0388f09f2883bf1ca6f50283b41bfeb6be6ddcc4e420144c16"),
                new PubKey("037b5f0a88a477d9fba812826a3bf43104ca078fc51b62c0eaad15d0f9a724a4b2"),
                new PubKey("027e793fbf4f6d07de15b0aa8355f88759b8bdf92a9ffb8a65a87fa8ee03baeccd"),
                new PubKey("028e1d9fd64b84a2ec85fac7185deb2c87cc0dd97270cf2d8adc3aa766dde975a7"),
                new PubKey("03535a285d0919a9bd71df3b274cecb46e16b78bf50d3bf8b0a3b41028cf8a842d"),
                new PubKey("0200c70e46cd94012caaae3fcc124e5f280f63a29cd2b3e15c15bac9d371da1e0d"),
                new PubKey("03eb5db0b1703ea7418f0ad20582bf8de0b4105887d232c7724f43f19f14862488"),
                new PubKey("03d8b5580b7ec709c006ef497327db27ea323bd358ca45412171c644214483b74f"),
                new PubKey("02ace4fbe6a622cdfc922a447c3253e8635f3fecb69241f73629e6f0596a567907"),
                new PubKey("03e8809be396745434ee8c875089e518a3eef40e31ade81869ce9cbef63484996d"),
                new PubKey("03a37019d2e010b046ef9d0459e4844a015758007602ddfbdc9702534924a23695"),
                new PubKey("0336312e7dce4f9ff8449a5d7d140be26eea7849f8ba13bb07b57b154a74aa7600"),
                new PubKey("038e1a76f0e33474144b61e0796404821a5150c00b05aad8a1cd502c865d8b5b92")}));

            var consensusFactory = new PosConsensusFactory();

             // Create the genesis block.
            this.GenesisTime = 1604913812; // ~9 November 2020 - https://www.unixtimestamp.com/
            this.GenesisNonce = 747342; // Set to 1 until correct value found
            this.GenesisBits = 0x1e0fffff; // The difficulty target
            this.GenesisVersion = 536870912; // 'Empty' BIP9 deployments as they are all activated from genesis already
            this.GenesisReward = Money.Zero;

            Block genesisBlock = StraxNetwork.CreateGenesisBlock(consensusFactory, this.GenesisTime, this.GenesisNonce, this.GenesisBits, this.GenesisVersion, this.GenesisReward, "stratisplatform.com/2020/09/25/introducing-strax/");

            this.Genesis = genesisBlock;

            // Taken from Stratis.
            var consensusOptions = new PosConsensusOptions(
                maxBlockBaseSize: 1_000_000,
                maxStandardVersion: 2,
                maxStandardTxWeight: 100_000,
                maxBlockSigopsCost: 20_000,
                maxStandardTxSigopsCost: 20_000 / 5,
                witnessScaleFactor: 4
            );

            var buriedDeployments = new BuriedDeploymentsArray
            {
                [BuriedDeployments.BIP34] = 0,
                [BuriedDeployments.BIP65] = 0,
                [BuriedDeployments.BIP66] = 0
            };

            var bip9Deployments = new StraxBIP9Deployments()
            {
                // Always active.
                [StraxBIP9Deployments.CSV] = new BIP9DeploymentsParameters("CSV", 0, BIP9DeploymentsParameters.AlwaysActive, 999999999, BIP9DeploymentsParameters.DefaultMainnetThreshold),
                [StraxBIP9Deployments.Segwit] = new BIP9DeploymentsParameters("Segwit", 1, BIP9DeploymentsParameters.AlwaysActive, 999999999, BIP9DeploymentsParameters.DefaultMainnetThreshold),
                [StraxBIP9Deployments.ColdStaking] = new BIP9DeploymentsParameters("ColdStaking", 2, BIP9DeploymentsParameters.AlwaysActive, 999999999, BIP9DeploymentsParameters.DefaultMainnetThreshold)
            };

            this.Consensus = new NBitcoin.Consensus(
                consensusFactory: consensusFactory,
                consensusOptions: consensusOptions,
                coinType: 105105, // https://github.com/satoshilabs/slips/blob/master/slip-0044.md
                hashGenesisBlock: genesisBlock.GetHash(),
                subsidyHalvingInterval: 210000,
                majorityEnforceBlockUpgrade: 750,
                majorityRejectBlockOutdated: 950,
                majorityWindow: 1000,
                buriedDeployments: buriedDeployments,
                bip9Deployments: bip9Deployments,
                bip34Hash: null,
                minerConfirmationWindow: 2016,
                maxReorgLength: 500,
                defaultAssumeValid: null, // TODO: Set this once some checkpoint candidates have elapsed
                maxMoney: long.MaxValue,
                coinbaseMaturity: 50,
                premineHeight: 2,
                premineReward: Money.Coins(125000000),
                proofOfWorkReward: Money.Coins(18),
                powTargetTimespan: TimeSpan.FromSeconds(14 * 24 * 60 * 60),
                targetSpacing: TimeSpan.FromSeconds(45),
                powAllowMinDifficultyBlocks: false,
                posNoRetargeting: false,
                powNoRetargeting: false,
                powLimit: new Target(new uint256("00000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")),
                minimumChainWork: null,
                isProofOfStake: true,
                lastPowBlock: 675,
                proofOfStakeLimit: new BigInteger(uint256.Parse("00000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffff").ToBytes(false)),
                proofOfStakeLimitV2: new BigInteger(uint256.Parse("000000000000ffffffffffffffffffffffffffffffffffffffffffffffffffff").ToBytes(false)),
                proofOfStakeReward: Money.Coins(18)
            );

            this.Consensus.PosEmptyCoinbase = false;

            this.Base58Prefixes = new byte[12][];
            this.Base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { 75 }; // X
            this.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { 140 }; // y
            this.Base58Prefixes[(int)Base58Type.SECRET_KEY] = new byte[] { (75 + 128) };
            this.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_NO_EC] = new byte[] { 0x01, 0x42 };
            this.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_EC] = new byte[] { 0x01, 0x43 };
            this.Base58Prefixes[(int)Base58Type.EXT_PUBLIC_KEY] = new byte[] { (0x04), (0x88), (0xB2), (0x1E) };
            this.Base58Prefixes[(int)Base58Type.EXT_SECRET_KEY] = new byte[] { (0x04), (0x88), (0xAD), (0xE4) };
            this.Base58Prefixes[(int)Base58Type.PASSPHRASE_CODE] = new byte[] { 0x2C, 0xE9, 0xB3, 0xE1, 0xFF, 0x39, 0xE2 };
            this.Base58Prefixes[(int)Base58Type.CONFIRMATION_CODE] = new byte[] { 0x64, 0x3B, 0xF6, 0xA8, 0x9A };
            this.Base58Prefixes[(int)Base58Type.STEALTH_ADDRESS] = new byte[] { 0x2a };
            this.Base58Prefixes[(int)Base58Type.ASSET_ID] = new byte[] { 23 };
            this.Base58Prefixes[(int)Base58Type.COLORED_ADDRESS] = new byte[] { 0x13 };

            this.Checkpoints = new Dictionary<int, CheckpointInfo>
            {
            };

            this.Bech32Encoders = new Bech32Encoder[2];
            var encoder = new Bech32Encoder("strax");
            this.Bech32Encoders[(int)Bech32Type.WITNESS_PUBKEY_ADDRESS] = encoder;
            this.Bech32Encoders[(int)Bech32Type.WITNESS_SCRIPT_ADDRESS] = encoder;

            this.DNSSeeds = new List<DNSSeedData>
            {
                new DNSSeedData("mainnet1.stratisnetwork.com", "mainnet1.stratisnetwork.com")
            };

            this.SeedNodes = new List<NetworkAddress>
            {
            };

            this.StandardScriptsRegistry = new StraxStandardScriptsRegistry();

            Assert(this.DefaultBanTimeSeconds <= this.Consensus.MaxReorgLength * this.Consensus.TargetSpacing.TotalSeconds / 2);

            // TODO: Update these when the final block is mined
            Assert(this.Consensus.HashGenesisBlock == uint256.Parse("0xebe158d09325c470276619ebc5f7f87c98c0ed4b211c46a17a6457655811d082"));
            Assert(this.Genesis.Header.HashMerkleRoot == uint256.Parse("0xdd91e99b7ca5eb97d9c41b867762d1f2db412ba4331efb61d138fce5d39b9084"));

            StraxNetwork.RegisterRules(this.Consensus);
            StraxNetwork.RegisterMempoolRules(this.Consensus);
        }
    }
}
