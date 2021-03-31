using System;
using System.Collections.Generic;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Protocol;
using Stratis.Bitcoin.Features.Consensus.Rules.CommonRules;
using Stratis.Bitcoin.Features.MemoryPool.Rules;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.BasePoAFeatureConsensusRules;
using Stratis.Bitcoin.Features.PoA.Policies;
using Stratis.Bitcoin.Features.PoA.Voting.ConsensusRules;
using Stratis.Bitcoin.Features.SmartContracts.MempoolRules;
using Stratis.Bitcoin.Features.SmartContracts.PoA;
using Stratis.Bitcoin.Features.SmartContracts.PoA.Rules;
using Stratis.Bitcoin.Features.SmartContracts.Rules;

namespace Stratis.Sidechains.Networks
{
    /// <summary>
    /// Right now, ripped nearly straight from <see cref="PoANetwork"/>.
    /// </summary>
    public class CirrusTest : PoANetwork
    {
        public CirrusTest()
        {
            this.Name = "CirrusTest";
            this.NetworkType = NetworkType.Testnet;
            this.CoinTicker = "TCRS";
            this.Magic = 0x522357B;
            this.DefaultPort = 26179;
            this.DefaultMaxOutboundConnections = 16;
            this.DefaultMaxInboundConnections = 109;
            this.DefaultRPCPort = 26175;
            this.DefaultAPIPort = 38223;
            this.DefaultSignalRPort = 39823;
            this.MaxTipAge = 768; // 20% of the fastest time it takes for one MaxReorgLength of blocks to be mined.
            this.MinTxFee = 10000;
            this.FallbackFee = 10000;
            this.MinRelayTxFee = 10000;
            this.RootFolderName = CirrusNetwork.NetworkRootFolderName;
            this.DefaultConfigFilename = CirrusNetwork.NetworkDefaultConfigFilename;
            this.MaxTimeOffsetSeconds = 25 * 60;
            this.DefaultBanTimeSeconds = 1920; // 240 (MaxReorg) * 16 (TargetSpacing) / 2 = 32 Minutes

            this.CirrusRewardDummyAddress = "tGXZrZiU44fx3SQj8tAQ3Zexy2VuELZtoh";

            var consensusFactory = new SmartContractCollateralPoAConsensusFactory();

            // Create the genesis block.
            this.GenesisTime = 1556631753;
            this.GenesisNonce = 146421;
            this.GenesisBits = new Target(new uint256("0000ffff00000000000000000000000000000000000000000000000000000000"));
            this.GenesisVersion = 1;
            this.GenesisReward = Money.Zero;

            string coinbaseText = "https://github.com/stratisproject/StratisBitcoinFullNode/tree/master/src/Stratis.CirrusD";
            Block genesisBlock = CirrusNetwork.CreateGenesis(consensusFactory, this.GenesisTime, this.GenesisNonce, this.GenesisBits, this.GenesisVersion, this.GenesisReward, coinbaseText);

            this.Genesis = genesisBlock;

            // Configure federation public keys (mining keys) used to sign blocks.
            // Keep in mind that order in which keys are added to this list is important
            // and should be the same for all nodes operating on this network.
            var genesisFederationMembers = new List<IFederationMember>()
            {
                new CollateralFederationMember(new PubKey("03cfc06ef56352038e1169deb3b4fa228356e2a54255cf77c271556d2e2607c28c"), true, new Money(50000_00000000), "qdKBKmoFWpuuxNHjoXK4tzTtuinwnjwH2Q"),//Node1
                new CollateralFederationMember(new PubKey("022553fb641898be98e6e331d644c1689455536e58ad643d84844e981708da38e9"), true, new Money(50000_00000000), "qPcHHH5ACc8282y95NWtaDz1xGuBvMyfgW"),//Node2
                new CollateralFederationMember(new PubKey("02fc828e06041ae803ab5378b5ec4e0def3d4e331977a69e1b6ef694d67f5c9c13"), true, new Money(50000_00000000), "qcEu17QYBmCqXjm9n7uCkaSDXyDAVmNzk2"),//Node3
                new CollateralFederationMember(new PubKey("02fd4f3197c40d41f9f5478d55844f522744258ca4093b5119571de1a5df1bc653"), true, new Money(50000_00000000), "qNBd9pwrPoJ3noK9SVUo2vuzrkAQ2uqd3u"),//Node4
                new CollateralFederationMember(new PubKey("030ac8e3e119257aff4512ea44450632a6a9b54104f936732d31c28a63a2104064"), true, new Money(50000_00000000), "qViLjFAvpEh7J1yGfPwC1bmPPEGN4vDkY6"),//Node5
                new CollateralFederationMember(new PubKey("03348a438f86727c579febfd6a656cfd6477605e5fa00efa5b4f5fe1cab01c49ef"), true, new Money(0), null),                                //Node6
                new CollateralFederationMember(new PubKey("024142689f38fdb5e8faf3bc7bc5065ecaad6be93a34055ffce0554f9268639c98"), true, new Money(50000_00000000), "qNS4f96BMzE8Z48ib5fprHxXgoTAXEqQck"),//Node7
                new CollateralFederationMember(new PubKey("03382ceb0a59b9b922aca6be9959ae51dabda159e79465393a308ee267ecebcaa5"), true, new Money(50000_00000000), "qgWAP4RQmuv6EB7ijtGFG4pDgD9qBKMWwY"),//Node8
                new CollateralFederationMember(new PubKey("027d31e9dc3ee5a42b1273ae8184e716fc616fed6d7b62323fa0a33901d188cfeb"), true, new Money(0), null),                                //Node9
                new CollateralFederationMember(new PubKey("02ef5a8167276ade598460e0c102cb216071e8430d55f10788979d8820fe2440b6"), true, new Money(0), null),                                //Node10
                new CollateralFederationMember(new PubKey("03d8e88797b56894a0d8ce6421defd4572fc8d19e18321d07ea22a6adec59f7fd1"), true, new Money(0), null),                                //Node11
                new CollateralFederationMember(new PubKey("0357c1f34d11e6a93d4e158e109ed5309ae77981a4968c23c975aa7640fe913429"), true, new Money(0), null),                                //Node12
                new CollateralFederationMember(new PubKey("0260c88a2fed5b615abcbde67e62762e2aa224460bd7918ca9d9f42ddfc1f63d08"), true, new Money(0), null),                                //Node13
                new CollateralFederationMember(new PubKey("020432898887bcc515b20d5d3dcea0ee86c700a3279c8d773caf37b5c317b4e2b4"), true, new Money(0), null),                                //Node14
                new CollateralFederationMember(new PubKey("02f9b73070474b7cfb3e6c2624c069cdbd211954f82862505f10cf0a2c3a45e7c5"), true, new Money(0), null),                                //Node15
            };

            this.Federations = new Federations();
            var straxFederationTransactionSigningKeys = new List<PubKey>()
            {
               new PubKey("021040ef28c82fcffb63028e69081605ed4712910c8384d5115c9ffeacd9dbcae4"),//Node1
               new PubKey("0244290a31824ba7d53e59c7a29d13dbeca15a9b0d36fdd4d28fce426753107bfc"),//Node2
               new PubKey("032df4a2d62c0db12cd1d66201819a10788637c9b90a1cd2a5a3f5196fdab7a621"),//Node3
               new PubKey("028ed190eb4ed6e46440ac6af21d8a67a537bd1bd7edb9cc5177d36d5a0972244d"),//Node4
               new PubKey("02ff9923324399a188daf4310825a85dd3b89e2301d0ad073295b6f33ae1c72f7a"),//Node5
               new PubKey("030e03b808ddb51701d4d3dbc0a74a6f9aedfecf23d5f874914641fc81197b239a"),//Node7
               new PubKey("02270d6c20d3393fad7f74c59d2d26b0824ed016ccbc15e698e7354314459a60a5"),//Node8
            };

            // Register the new set of federation members.
            this.Federations.RegisterFederation(new Federation(straxFederationTransactionSigningKeys));

            // Set the list of Strax Era mining keys.
            this.StraxMiningMultisigMembers = new List<PubKey>()
            {
                new PubKey("03cfc06ef56352038e1169deb3b4fa228356e2a54255cf77c271556d2e2607c28c"),//Node1
                new PubKey("022553fb641898be98e6e331d644c1689455536e58ad643d84844e981708da38e9"),//Node2
                new PubKey("02fc828e06041ae803ab5378b5ec4e0def3d4e331977a69e1b6ef694d67f5c9c13"),//Node3
                new PubKey("02fd4f3197c40d41f9f5478d55844f522744258ca4093b5119571de1a5df1bc653"),//Node4
                new PubKey("030ac8e3e119257aff4512ea44450632a6a9b54104f936732d31c28a63a2104064"),//Node5
                new PubKey("024142689f38fdb5e8faf3bc7bc5065ecaad6be93a34055ffce0554f9268639c98"),//Node7
                new PubKey("03382ceb0a59b9b922aca6be9959ae51dabda159e79465393a308ee267ecebcaa5"),//Node8
            };

            var consensusOptions = new PoAConsensusOptions(
                maxBlockBaseSize: 1_000_000,
                maxStandardVersion: 2,
                maxStandardTxWeight: 150_000,
                maxBlockSigopsCost: 20_000,
                maxStandardTxSigopsCost: 20_000 / 5,
                genesisFederationMembers: genesisFederationMembers,
                targetSpacingSeconds: 16,
                votingEnabled: true,
                autoKickIdleMembers: true,
                federationMemberMaxIdleTimeSeconds: 60 * 60 * 3 // 3 Hours
            )
            {
                EnforceMinProtocolVersionAtBlockHeight = 505900, // setting the value to zero makes the functionality inactive
                EnforcedMinProtocolVersion = ProtocolVersion.CIRRUS_VERSION, // minimum protocol version which will be enforced at block height defined in EnforceMinProtocolVersionAtBlockHeight
                VotingManagerV2ActivationHeight = 1_999_500
            };

            var buriedDeployments = new BuriedDeploymentsArray
            {
                [BuriedDeployments.BIP34] = 0,
                [BuriedDeployments.BIP65] = 0,
                [BuriedDeployments.BIP66] = 0
            };

            var bip9Deployments = new NoBIP9Deployments();

            this.Consensus = new Consensus(
                consensusFactory: consensusFactory,
                consensusOptions: consensusOptions,
                coinType: 400,
                hashGenesisBlock: genesisBlock.GetHash(),
                subsidyHalvingInterval: 210000,
                majorityEnforceBlockUpgrade: 750,
                majorityRejectBlockOutdated: 950,
                majorityWindow: 1000,
                buriedDeployments: buriedDeployments,
                bip9Deployments: bip9Deployments,
                bip34Hash: new uint256("0x000000000000024b89b42a942fe0d9fea3bb44ab7bd1b19115dd6a759c0808b8"),
                minerConfirmationWindow: 2016, // nPowTargetTimespan / nPowTargetSpacing
                maxReorgLength: 240, // Heuristic. Roughly 2 * mining members
                defaultAssumeValid: new uint256("0x57a3119de52cf43b66d6e805a644c20fdee63557038cd68c429d47b21d111084"), // 1800000
                maxMoney: Money.Coins(20_000_000),
                coinbaseMaturity: 1,
                premineHeight: 2,
                premineReward: Money.Coins(20_000_000),
                proofOfWorkReward: Money.Coins(0),
                powTargetTimespan: TimeSpan.FromDays(14), // two weeks
                targetSpacing: TimeSpan.FromSeconds(16),
                powAllowMinDifficultyBlocks: false,
                posNoRetargeting: false,
                powNoRetargeting: true,
                powLimit: null,
                minimumChainWork: null,
                isProofOfStake: false,
                lastPowBlock: 0,
                proofOfStakeLimit: null,
                proofOfStakeLimitV2: null,
                proofOfStakeReward: Money.Zero
            );

            // Same as current smart contracts test networks to keep tests working
            this.Base58Prefixes = new byte[12][];
            this.Base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { 127 }; // t
            this.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { 137 }; // x
            this.Base58Prefixes[(int)Base58Type.SECRET_KEY] = new byte[] { (239) };
            this.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_NO_EC] = new byte[] { 0x01, 0x42 };
            this.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_EC] = new byte[] { 0x01, 0x43 };
            this.Base58Prefixes[(int)Base58Type.EXT_PUBLIC_KEY] = new byte[] { (0x04), (0x35), (0x87), (0xCF) };
            this.Base58Prefixes[(int)Base58Type.EXT_SECRET_KEY] = new byte[] { (0x04), (0x35), (0x83), (0x94) };
            this.Base58Prefixes[(int)Base58Type.PASSPHRASE_CODE] = new byte[] { 0x2C, 0xE9, 0xB3, 0xE1, 0xFF, 0x39, 0xE2 };
            this.Base58Prefixes[(int)Base58Type.CONFIRMATION_CODE] = new byte[] { 0x64, 0x3B, 0xF6, 0xA8, 0x9A };
            this.Base58Prefixes[(int)Base58Type.STEALTH_ADDRESS] = new byte[] { 0x2b };
            this.Base58Prefixes[(int)Base58Type.ASSET_ID] = new byte[] { 115 };
            this.Base58Prefixes[(int)Base58Type.COLORED_ADDRESS] = new byte[] { 0x13 };

            Bech32Encoder encoder = Encoders.Bech32("tb");
            this.Bech32Encoders = new Bech32Encoder[2];
            this.Bech32Encoders[(int)Bech32Type.WITNESS_PUBKEY_ADDRESS] = encoder;
            this.Bech32Encoders[(int)Bech32Type.WITNESS_SCRIPT_ADDRESS] = encoder;

            this.Checkpoints = new Dictionary<int, CheckpointInfo>()
            {
                { 50000, new CheckpointInfo(new uint256("0x2b2a85fcad21c4e5c91a7afef04dce2eb72426b0c6275d87669a561f9f6db1f3")) },
                { 100000, new CheckpointInfo(new uint256("0x364be98c01780accfea63c52703b7dc4731fdead1b6769cf9a893b4e6c736f10")) },
                { 150000, new CheckpointInfo(new uint256("0xaf862418d54d95221dac077cdbd0d49d68304d67721df7b44775739f093985f1")) },
                { 200000, new CheckpointInfo(new uint256("0x40f99ccbd290c2c66c16eac602b4a8b4dc7d87bfceb31c64ae5942d5899e86b2")) },
                { 250000, new CheckpointInfo(new uint256("0x33deee954579b8b3ffde1d9246a3e9e548dc7f4f8c0c9cbf206eb14ac04ab500")) },
                { 300000, new CheckpointInfo(new uint256("0x1c1670f9ea4d211abe255a516be95ec6329d03e0ebfc81890cae0900e9f07964")) },
                { 350000, new CheckpointInfo(new uint256("0x5b3493243a9f8c8997acad7cb13058e11a9d2d91c9494ebe5c88446540640472")) },
                { 400000, new CheckpointInfo(new uint256("0x33d57af0bc04916eb43f6d5c3f0b97b0f281662feac3d03b987bb9ab4978fe0a")) },
                { 450000, new CheckpointInfo(new uint256("0x7c85cc3aa0694c7573b1455e555c9f6a919dfa916381d6c094cdc2da46a0c7bc")) },
                { 500000, new CheckpointInfo(new uint256("0x3f00eb415856128976e786cb094e88d4dfaabfedea462498386e201c1ac2a1fa")) },
                { 550000, new CheckpointInfo(new uint256("0xaaf247bd66568db8945fc8947525539160073bcfb4a60a09d23fdcbf4d775a15")) },
                { 600000, new CheckpointInfo(new uint256("0x610a60579898e9160509ea4453cb946e1fdb9ebc18eedffd77513f42a61c0d77")) },
                { 700000, new CheckpointInfo(new uint256("0x6d5addc975a93eb323933bcdf2c3b7e098e324e8b205232a490cd585aceb1518")) },
                { 800000, new CheckpointInfo(new uint256("0x6ff2a00696e1601efba88b98ef63e691e8da7acffd5703614e971c932d93af80")) },
                { 900000, new CheckpointInfo(new uint256("0x84b550eafbfe777d28321eabed9a118a3175bcd607481bbfe24dc5fa2a9de0cf")) },
                { 1000000, new CheckpointInfo(new uint256("0xc3da5b782bdf6b9d0606147996479b0ea621322d9df1d239cbbd814175f4ed61")) },
                { 1100000, new CheckpointInfo(new uint256("0xbb2d946fa7101c14c6374b0e40993ef73401a360e74652e1677d8d6b3b4be01c")) },
                { 1200000, new CheckpointInfo(new uint256("0x8b7c48e0e814afbedb0d6e67dc71aaa395886db58e17fd622e571e1d140fbbb3")) },
                { 1300000, new CheckpointInfo(new uint256("0xe4aecd9ecdbf4e55b08255ed6d8a98e811fbd3e7c72ef267c26ebfae4e315990")) },
                { 1400000, new CheckpointInfo(new uint256("0x7165b03c170869b318253d470aa904f9c674c0d0f4ca2e9a64416b1d42beecc5")) },
                { 1500000, new CheckpointInfo(new uint256("0xb458117f195f936d7767f7299d0976ad90700e321870c18ec1e3481924f2afc3")) },
                { 1600000, new CheckpointInfo(new uint256("0x696cd64ec08b67ed3a3ec1e3add77c0e8203d8d6c0bb7df96dd9508dda4ba67e")) },
                { 1700000, new CheckpointInfo(new uint256("0xf42564107701d81e847e5dc6bd95da6bf32cb54e762d84118a7764349b414e68")) },
                { 1800000, new CheckpointInfo(new uint256("0x57a3119de52cf43b66d6e805a644c20fdee63557038cd68c429d47b21d111084")) },
                { 1900000, new CheckpointInfo(new uint256("0xd413f3aed50f4a1a4580e7c506223a605e222849da9649ca6d43ad7aac5c5af5")) },
            };

            this.DNSSeeds = new List<DNSSeedData>
            {
                new DNSSeedData("cirrustest1.stratisnetwork.com", "cirrustest1.stratisnetwork.com")
            };

            this.SeedNodes = new List<NetworkAddress>();

            this.StandardScriptsRegistry = new PoAStandardScriptsRegistry();

            this.CollateralCommitmentActivationHeight = 25810;

            Assert(this.DefaultBanTimeSeconds <= this.Consensus.MaxReorgLength * this.Consensus.TargetSpacing.TotalSeconds / 2);
            Assert(this.Consensus.HashGenesisBlock == uint256.Parse("0000af9ab2c8660481328d0444cf167dfd31f24ca2dbba8e5e963a2434cffa93"));
            Assert(this.Genesis.Header.HashMerkleRoot == uint256.Parse("cf8ce1419bbc4870b7d4f1c084534d91126dd3283b51ec379e0a20e27bd23633"));

            this.RegisterRules(this.Consensus);
            this.RegisterMempoolRules(this.Consensus);
        }

        // This should be abstract or virtual
        protected override void RegisterRules(IConsensus consensus)
        {
            // IHeaderValidationConsensusRule -----------------------
            consensus.ConsensusRules
                .Register<HeaderTimeChecksPoARule>()
                .Register<StratisHeaderVersionRule>()
                .Register<PoAHeaderDifficultyRule>();
            // ------------------------------------------------------

            // IIntegrityValidationConsensusRule
            consensus.ConsensusRules
                .Register<BlockMerkleRootRule>()
                .Register<PoAIntegritySignatureRule>();
            // ------------------------------------------------------

            // IPartialValidationConsensusRule
            consensus.ConsensusRules
                .Register<SetActivationDeploymentsPartialValidationRule>()

                // Rules that are inside the method ContextualCheckBlock
                .Register<TransactionLocktimeActivationRule>()
                .Register<CoinbaseHeightActivationRule>()
                .Register<BlockSizeRule>()

                // Rules that are inside the method CheckBlock
                .Register<EnsureCoinbaseRule>()
                .Register<CheckPowTransactionRule>()
                .Register<CheckSigOpsRule>()

                .Register<PoAVotingCoinbaseOutputFormatRule>()
                .Register<AllowedScriptTypeRule>()
                .Register<ContractTransactionPartialValidationRule>();
            // ------------------------------------------------------

            // IFullValidationConsensusRule
            consensus.ConsensusRules
                .Register<SetActivationDeploymentsFullValidationRule>()

                // Rules that require the store to be loaded (coinview)
                .Register<PoAHeaderSignatureRule>()
                .Register<LoadCoinviewRule>()
                .Register<TransactionDuplicationActivationRule>() // implements BIP30

                // Smart contract specific
                .Register<ContractTransactionFullValidationRule>()
                .Register<TxOutSmartContractExecRule>()
                .Register<OpSpendRule>()
                .Register<CanGetSenderRule>()
                .Register<P2PKHNotContractRule>()
                .Register<SmartContractPoACoinviewRule>()
                .Register<SaveCoinviewRule>();
            // ------------------------------------------------------
        }

        protected override void RegisterMempoolRules(IConsensus consensus)
        {
            consensus.MempoolRules = new List<Type>()
            {
                typeof(OpSpendMempoolRule),
                typeof(TxOutSmartContractExecMempoolRule),
                typeof(AllowedScriptTypeMempoolRule),
                typeof(P2PKHNotContractMempoolRule),

                // The non- smart contract mempool rules
                typeof(CheckConflictsMempoolRule),
                typeof(CheckCoinViewMempoolRule),
                typeof(CreateMempoolEntryMempoolRule),
                typeof(CheckSigOpsMempoolRule),
                typeof(CheckFeeMempoolRule),

                // The smart contract mempool needs to do more fee checks than its counterpart, so include extra rules.
                // These rules occur directly after the fee check rule in the non- smart contract mempool.
                typeof(SmartContractFormatLogicMempoolRule),
                typeof(CanGetSenderMempoolRule),
                typeof(AllowedCodeHashLogicMempoolRule), // PoA-specific
                typeof(CheckMinGasLimitSmartContractMempoolRule),

                // Remaining non-SC rules.
                typeof(CheckRateLimitMempoolRule),
                typeof(CheckAncestorsMempoolRule),
                typeof(CheckReplacementMempoolRule),
                typeof(CheckAllInputsMempoolRule)
            };
        }
    }
}
