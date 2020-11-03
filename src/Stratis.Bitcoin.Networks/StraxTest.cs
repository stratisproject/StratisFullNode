using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using NBitcoin;
using NBitcoin.BouncyCastle.Math;
using NBitcoin.DataEncoders;
using NBitcoin.Protocol;
using Stratis.Bitcoin.Networks.Deployments;
using Stratis.Bitcoin.Networks.Policies;

namespace Stratis.Bitcoin.Networks
{
    public sealed class StraxTest : Network
    {
        public StraxTest()
        {
            this.Name = "StraxTest";
            this.NetworkType = NetworkType.Testnet;
            this.Magic = BitConverter.ToUInt32(Encoding.ASCII.GetBytes("TtrX"));
            this.DefaultPort = 27105;
            this.DefaultMaxOutboundConnections = 16;
            this.DefaultMaxInboundConnections = 100;
            this.DefaultRPCPort = 27104;
            this.DefaultAPIPort = 27103;
            this.DefaultSignalRPort = 27102;
            this.MaxTipAge = 2 * 60 * 60;
            this.MinTxFee = 10000;
            this.FallbackFee = 10000;
            this.MinRelayTxFee = 10000;
            this.RootFolderName = StraxNetwork.StraxRootFolderName;
            this.DefaultConfigFilename = StraxNetwork.StraxDefaultConfigFilename;
            this.MaxTimeOffsetSeconds = 25 * 60;
            this.CoinTicker = "TSTRAX";
            this.DefaultBanTimeSeconds = 11250; // 500 (MaxReorg) * 45 (TargetSpacing) / 2 = 3 hours, 7 minutes and 30 seconds

            this.CirrusRewardDummyAddress = "PDpvfcpPm9cjQEoxWzQUL699N8dPaf8qML"; // Cirrus test address

            var consensusFactory = new PosConsensusFactory();

            // Create the genesis block.
            this.GenesisTime = 1598918400; // 1 September 2020
            this.GenesisNonce = 109534; // TODO: Update this once the final block is mined
            this.GenesisBits = new Target(new uint256("0000ffff00000000000000000000000000000000000000000000000000000000")).ToCompact(); // This should be set to the same as the PowLimit
            this.GenesisVersion = 1;
            this.GenesisReward = Money.Zero;

            Block genesisBlock = StraxNetwork.CreateGenesisBlock(consensusFactory, this.GenesisTime, this.GenesisNonce, this.GenesisBits, this.GenesisVersion, this.GenesisReward, "teststraxgenesisblock");

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
                [StraxBIP9Deployments.CSV] = new BIP9DeploymentsParameters("CSV", 0, BIP9DeploymentsParameters.AlwaysActive, 999999999, BIP9DeploymentsParameters.DefaultTestnetThreshold),
                [StraxBIP9Deployments.Segwit] = new BIP9DeploymentsParameters("Segwit", 1, BIP9DeploymentsParameters.AlwaysActive, 999999999, BIP9DeploymentsParameters.DefaultTestnetThreshold),
                [StraxBIP9Deployments.ColdStaking] = new BIP9DeploymentsParameters("ColdStaking", 2, BIP9DeploymentsParameters.AlwaysActive, 999999999, BIP9DeploymentsParameters.DefaultTestnetThreshold)
            };

            // To successfully process the OP_FEDERATION opcode the federations should be known.
            this.Federations = new Federations();

            // Cirrus federation.
            this.Federations.RegisterFederation(new Federation(new[] {
                new PubKey("03cfc06ef56352038e1169deb3b4fa228356e2a54255cf77c271556d2e2607c28c"),
                new PubKey("022553fb641898be98e6e331d644c1689455536e58ad643d84844e981708da38e9"),
                new PubKey("02fc828e06041ae803ab5378b5ec4e0def3d4e331977a69e1b6ef694d67f5c9c13"),
                new PubKey("02fd4f3197c40d41f9f5478d55844f522744258ca4093b5119571de1a5df1bc653"),
                new PubKey("030ac8e3e119257aff4512ea44450632a6a9b54104f936732d31c28a63a2104064"),
                new PubKey("03348a438f86727c579febfd6a656cfd6477605e5fa00efa5b4f5fe1cab01c49ef"),
                new PubKey("024142689f38fdb5e8faf3bc7bc5065ecaad6be93a34055ffce0554f9268639c98"),
                new PubKey("03382ceb0a59b9b922aca6be9959ae51dabda159e79465393a308ee267ecebcaa5"),
                new PubKey("027d31e9dc3ee5a42b1273ae8184e716fc616fed6d7b62323fa0a33901d188cfeb"),
                new PubKey("02ef5a8167276ade598460e0c102cb216071e8430d55f10788979d8820fe2440b6"),
                new PubKey("03d8e88797b56894a0d8ce6421defd4572fc8d19e18321d07ea22a6adec59f7fd1"),
                new PubKey("0357c1f34d11e6a93d4e158e109ed5309ae77981a4968c23c975aa7640fe913429"),
                new PubKey("0260c88a2fed5b615abcbde67e62762e2aa224460bd7918ca9d9f42ddfc1f63d08"),
                new PubKey("020432898887bcc515b20d5d3dcea0ee86c700a3279c8d773caf37b5c317b4e2b4"),
                new PubKey("02f9b73070474b7cfb3e6c2624c069cdbd211954f82862505f10cf0a2c3a45e7c5")
            }));

            this.Consensus = new NBitcoin.Consensus(
                consensusFactory: consensusFactory,
                consensusOptions: consensusOptions,
                coinType: 1, // Per https://github.com/satoshilabs/slips/blob/master/slip-0044.md - testnets share a cointype
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
                defaultAssumeValid: null,
                maxMoney: long.MaxValue,
                coinbaseMaturity: 50,
                premineHeight: 2,
                premineReward: Money.Coins(130000000),
                proofOfWorkReward: Money.Coins(18),
                powTargetTimespan: TimeSpan.FromSeconds(14 * 24 * 60 * 60),
                targetSpacing: TimeSpan.FromSeconds(45),
                powAllowMinDifficultyBlocks: false,
                posNoRetargeting: false,
                powNoRetargeting: false,
                powLimit: new Target(new uint256("0000ffff00000000000000000000000000000000000000000000000000000000")),
                minimumChainWork: null,
                isProofOfStake: true,
                lastPowBlock: 12500,
                proofOfStakeLimit: new BigInteger(uint256.Parse("00000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffff").ToBytes(false)),
                proofOfStakeLimitV2: new BigInteger(uint256.Parse("000000000000ffffffffffffffffffffffffffffffffffffffffffffffffffff").ToBytes(false)),
                proofOfStakeReward: Money.Coins(18)
            );

            this.Consensus.PosEmptyCoinbase = false;

            this.Base58Prefixes = new byte[12][];
            this.Base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { 120 }; // q
            this.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { 127 }; // t
            this.Base58Prefixes[(int)Base58Type.SECRET_KEY] = new byte[] { (120 + 128) };
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
            var encoder = new Bech32Encoder("tstrax");
            this.Bech32Encoders[(int)Bech32Type.WITNESS_PUBKEY_ADDRESS] = encoder;
            this.Bech32Encoders[(int)Bech32Type.WITNESS_SCRIPT_ADDRESS] = encoder;

            this.DNSSeeds = new List<DNSSeedData>
            {
                new DNSSeedData("testnet1.stratisnetwork.com", "testnet1.stratisnetwork.com")
            };

            this.SeedNodes = new List<NetworkAddress>
            {
                new NetworkAddress(IPAddress.Parse("82.146.153.140"), 27105), // Iain
            };

            this.StandardScriptsRegistry = new StraxStandardScriptsRegistry();

            Assert(this.DefaultBanTimeSeconds <= this.Consensus.MaxReorgLength * this.Consensus.TargetSpacing.TotalSeconds / 2);

            // TODO: Update these when the final block is mined
            Assert(this.Consensus.HashGenesisBlock == uint256.Parse("0x0000db68ff9e74fbaf7654bab4fa702c237318428fa9186055c243ddde6354ca"));
            Assert(this.Genesis.Header.HashMerkleRoot == uint256.Parse("0xfe6317d42149b091399e7f834ca32fd248f8f26f493c30a35d6eea692fe4fcad"));

            StraxNetwork.RegisterRules(this.Consensus);
            StraxNetwork.RegisterMempoolRules(this.Consensus);
        }
    }
}
