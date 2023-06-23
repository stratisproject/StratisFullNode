using System;
using System.Collections.Generic;
using System.Linq;
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
    public class CirrusRegTest : PoANetwork
    {
        public IList<Mnemonic> FederationMnemonics { get; }

        public CirrusRegTest()
        {
            this.Name = "CirrusRegTest";
            this.NetworkType = NetworkType.Regtest;
            this.CoinTicker = "TCRS";
            this.Magic = 0x522357C;
            this.DefaultPort = 26179;
            this.DefaultMaxOutboundConnections = 16;
            this.DefaultMaxInboundConnections = 109;
            this.DefaultRPCPort = 26175;
            this.DefaultAPIPort = 38223;
            this.MaxTipAge = 768; // 20% of the fastest time it takes for one MaxReorgLength of blocks to be mined.
            this.MinTxFee = 10000;
            this.FallbackFee = 10000;
            this.MinRelayTxFee = 10000;
            this.RootFolderName = CirrusNetwork.NetworkRootFolderName;
            this.DefaultConfigFilename = CirrusNetwork.NetworkDefaultConfigFilename;
            this.MaxTimeOffsetSeconds = 25 * 60;
            this.DefaultBanTimeSeconds = 1920; // 240 (MaxReorg) * 16 (TargetSpacing) / 2 = 32 Minutes

            this.CirrusRewardDummyAddress = "PDpvfcpPm9cjQEoxWzQUL699N8dPaf8qML";

            this.ConversionTransactionFeeDistributionDummyAddress = "PTCPsLQoF3WNoH1qXMy5PouquiXQKp7WBV";

            var consensusFactory = new SmartContractCollateralPoAConsensusFactory();

            // Create the genesis block.
            this.GenesisTime = 1545310504;
            this.GenesisNonce = 761900;
            this.GenesisBits = new Target(new uint256("0fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"));
            this.GenesisVersion = 1;
            this.GenesisReward = Money.Zero;

            string coinbaseText = "https://www.abc.net.au/news/science/2018-12-07/encryption-bill-australian-technology-industry-fuming-mad/10589962";
            Block genesisBlock = CirrusNetwork.CreateGenesis(consensusFactory, this.GenesisTime, this.GenesisNonce, this.GenesisBits, this.GenesisVersion, this.GenesisReward, coinbaseText);

            this.Genesis = genesisBlock;

            // Configure federation public keys used to sign blocks.
            // Keep in mind that order in which keys are added to this list is important
            // and should be the same for all nodes operating on this network.
            var genesisFederationMembers = new List<IFederationMember>()
            {
                new CollateralFederationMember(new PubKey("035791879dede5d312d30a880f4932ed011f2a038d6101663dbe6cd80ded280e53"), true, new Money(0), null),
                new CollateralFederationMember(new PubKey("025a1d22c4858d6ffb861a7541d12d196fce1f7d145aa4675325d8d440f3f8134b"), true, new Money(0), null),
                new CollateralFederationMember(new PubKey("02cfb22048508d84d1251a0f9803a140b2f0d37296fc306e81d3fe1818e32ded4c"), true, new Money(0), null)
            };

            // Register only the new federation as we won't be doing anything with the old federation.
            this.Federations = new Federations();
            var straxFederationTransactionSigningKeys = new List<PubKey>()
            {
                new PubKey("035791879dede5d312d30a880f4932ed011f2a038d6101663dbe6cd80ded280e53"),//Node1
                new PubKey("025a1d22c4858d6ffb861a7541d12d196fce1f7d145aa4675325d8d440f3f8134b"),//Node2
                new PubKey("02cfb22048508d84d1251a0f9803a140b2f0d37296fc306e81d3fe1818e32ded4c")//Node3
            };

            // Register the new set of federation members.
            this.Federations.RegisterFederation(new Federation(straxFederationTransactionSigningKeys));

            // The height at which the following list of members apply.
            this.MultisigMinersApplicabilityHeight = 0;

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
                federationMemberMaxIdleTimeSeconds: 60 * 60
                )
            {
                EnforceMinProtocolVersionAtBlockHeight = 1, // setting the value to zero makes the functionality inactive
                EnforcedMinProtocolVersion = ProtocolVersion.CIRRUS_VERSION, // minimum protocol version which will be enforced at block height defined in EnforceMinProtocolVersionAtBlockHeight
                InterFluxV2MainChainActivationHeight = 100,
                VotingManagerV2ActivationHeight = 100, // Tuesday, 12 January 2021 9:00:00 AM (Estimated)
                Release1100ActivationHeight = 200, // Monday, 20 December 2021 10:00:00 AM (Estimated)
                PollExpiryBlocks = 200, // Roughly 9 days
                GetMiningTimestampV2ActivationHeight = 100,
                GetMiningTimestampV2ActivationStrictHeight = 200,
                ContractSerializerV2ActivationHeight = 400, // Monday 13 December 16:00:00 (Estimated)
                Release1300ActivationHeight = 500,
                Release1400ActivationHeight = 800
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
                [CirrusBIP9Deployments.Release1320] = new BIP9DeploymentsParameters("Release1320", CirrusBIP9Deployments.FlagBitRelease1320, DateTime.Parse("2022-6-15 +0").ToUnixTimestamp() /* Activation date lower bound */, DateTime.Parse("2023-1-1 +0").ToUnixTimestamp(), BIP9DeploymentsParameters.DefaultTestnetThreshold),
                [CirrusBIP9Deployments.Release1324] = new BIP9DeploymentsParameters("Release1324", CirrusBIP9Deployments.FlagBitRelease1324, DateTime.Parse("2022-10-10 +0").ToUnixTimestamp() /* Activation date lower bound */, DateTime.Parse("2023-3-1 +0").ToUnixTimestamp(), BIP9DeploymentsParameters.DefaultTestnetThreshold)
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
                bip34Hash: null,
                minerConfirmationWindow: 2016, // nPowTargetTimespan / nPowTargetSpacing
                maxReorgLength: 240, // Heuristic. Roughly 2 * mining members
                defaultAssumeValid: null,
                maxMoney: Money.Coins(20_000_000),
                coinbaseMaturity: 1,
                premineHeight: 2,
                premineReward: Money.Coins(20_000_000),
                proofOfWorkReward: Money.Coins(0),
                powTargetTimespan: TimeSpan.FromDays(14), // two weeks
                targetSpacing: TimeSpan.FromSeconds(16),
                powAllowMinDifficultyBlocks: false,
                posNoRetargeting: true,
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
            this.Base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { 55 }; // P
            this.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { 117 }; // p
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

            this.Checkpoints = new Dictionary<int, CheckpointInfo>();

            this.DNSSeeds = new List<DNSSeedData>();
            this.SeedNodes = new List<NetworkAddress>();

            this.StandardScriptsRegistry = new PoAStandardScriptsRegistry();

            // 16 below should be changed to TargetSpacingSeconds when we move that field.
            Assert(this.DefaultBanTimeSeconds <= this.Consensus.MaxReorgLength * this.Consensus.TargetSpacing.TotalSeconds / 2);

            // TODO: Do we need Asserts for block hash

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
