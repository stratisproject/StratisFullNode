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
using Stratis.Bitcoin.Utilities.Extensions;
using Stratis.Sidechains.Networks.Deployments;

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

            this.ConversionTransactionFeeDistributionDummyAddress = "tUAzRBe1CaKaZnrxWPLVv7F4owHHKXAtbj";

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
                new CollateralFederationMember(new PubKey("035791879dede5d312d30a880f4932ed011f2a038d6101663dbe6cd80ded280e53"), true, new Money(0), null),
                new CollateralFederationMember(new PubKey("025a1d22c4858d6ffb861a7541d12d196fce1f7d145aa4675325d8d440f3f8134b"), true, new Money(0), null),
                new CollateralFederationMember(new PubKey("02cfb22048508d84d1251a0f9803a140b2f0d37296fc306e81d3fe1818e32ded4c"), true, new Money(0), null)
            };

            this.Federations = new Federations();
            /*
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
            */

            // The height at which the following list of members apply.
            this.MultisigMinersApplicabilityHeight = 1873798;

            // Set the list of Strax Era mining keys.
            this.StraxMiningMultisigMembers = new List<PubKey>()
            {
                new PubKey("035791879dede5d312d30a880f4932ed011f2a038d6101663dbe6cd80ded280e53"),//Node1
                new PubKey("025a1d22c4858d6ffb861a7541d12d196fce1f7d145aa4675325d8d440f3f8134b"),//Node2
                new PubKey("02cfb22048508d84d1251a0f9803a140b2f0d37296fc306e81d3fe1818e32ded4c")//Node3
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
                federationMemberMaxIdleTimeSeconds: 60 * 30 // 30 minutes
            )
            {
                InterFluxV2MainChainActivationHeight = 1,
                EnforceMinProtocolVersionAtBlockHeight = 1, // setting the value to zero makes the functionality inactive
                EnforcedMinProtocolVersion = ProtocolVersion.CIRRUS_VERSION, // minimum protocol version which will be enforced at block height defined in EnforceMinProtocolVersionAtBlockHeight
                VotingManagerV2ActivationHeight = 1,
                Release1100ActivationHeight = 1,
                PollExpiryBlocks = 450,
                GetMiningTimestampV2ActivationHeight = 1,
                GetMiningTimestampV2ActivationStrictHeight = 1, 
                ContractSerializerV2ActivationHeight = 1,
                Release1300ActivationHeight = 1,
                Release1400ActivationHeight = 1,
            };

            var buriedDeployments = new BuriedDeploymentsArray
            {
                [BuriedDeployments.BIP34] = 0,
                [BuriedDeployments.BIP65] = 0,
                [BuriedDeployments.BIP66] = 0
            };

            var bip9Deployments = new CirrusBIP9Deployments()
            {
                // Deployment will go active once 75% of nodes are on 1.3.0.0 or later.
                [CirrusBIP9Deployments.Release1320] = new BIP9DeploymentsParameters("Release1320", CirrusBIP9Deployments.FlagBitRelease1320, DateTime.Parse("2022-6-15 +0").ToUnixTimestamp() /* Activation date lower bound */, DateTime.Parse("2023-1-1 +0").ToUnixTimestamp(), BIP9DeploymentsParameters.DefaultRegTestThreshold),
                [CirrusBIP9Deployments.Release1324] = new BIP9DeploymentsParameters("Release1324", CirrusBIP9Deployments.FlagBitRelease1324, DateTime.Parse("2022-10-10 +0").ToUnixTimestamp() /* Activation date lower bound */, DateTime.Parse("2023-3-1 +0").ToUnixTimestamp(), BIP9DeploymentsParameters.DefaultRegTestThreshold)
            };

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
            };

            this.DNSSeeds = new List<DNSSeedData>
            {
                //new DNSSeedData("cirrustest1.stratisnetwork.com", "cirrustest1.stratisnetwork.com")
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
