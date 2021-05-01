using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NBitcoin;
using NBitcoin.BouncyCastle.Math;
using NBitcoin.DataEncoders;
using NBitcoin.Protocol;
using Stratis.Bitcoin.Features.Consensus.Rules.CommonRules;
using Stratis.Bitcoin.Features.Consensus.Rules.ProvenHeaderRules;
using Stratis.Bitcoin.Features.MemoryPool.Rules;
using Stratis.Bitcoin.Features.SmartContracts.MempoolRules;
using Stratis.Bitcoin.Features.SmartContracts.PoS;
using Stratis.Bitcoin.Features.SmartContracts.Rules;

namespace Stratis.SmartContracts.Networks
{
    public class SmartContractsPoSRegTest : Network
    {
        public SmartContractsPoSRegTest()
        {
            this.Name = "SmartContractsPoSRegTest";
            this.NetworkType = NetworkType.Regtest;
            this.Magic = BitConverter.ToUInt32(Encoding.ASCII.GetBytes("RtrX"));
            this.DefaultPort = 37105;
            this.DefaultMaxOutboundConnections = 16;
            this.DefaultMaxInboundConnections = 109;
            this.DefaultRPCPort = 37104;
            this.DefaultAPIPort = 37103;
            this.DefaultSignalRPort = 37102;
            this.MaxTipAge = 2 * 60 * 60;
            this.MinTxFee = 10000;
            this.FallbackFee = 10000;
            this.MinRelayTxFee = 10000;
            this.RootFolderName = "scpos";
            this.DefaultConfigFilename = "scpos.conf";
            this.MaxTimeOffsetSeconds = 25 * 60;
            this.CoinTicker = "SCPOS";
            this.DefaultBanTimeSeconds = 11250; // 500 (MaxReorg) * 45 (TargetSpacing) / 2 = 3 hours, 7 minutes and 30 seconds

            this.CirrusRewardDummyAddress = "PDpvfcpPm9cjQEoxWzQUL699N8dPaf8qML"; // Cirrus test address

            this.RewardClaimerBatchActivationHeight = 0;
            this.RewardClaimerBlockInterval = 100;

            var powLimit = new Target(new uint256("7fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"));

            var consensusFactory = new SmartContractPoSConsensusFactory();

            // Create the genesis block.
            this.GenesisTime = 1470467000; // TODO: Make this more recent?
            this.GenesisNonce = 1; // TODO: Update once the final block is mined
            this.GenesisBits = powLimit.ToCompact();
            this.GenesisVersion = 1;
            this.GenesisReward = Money.Zero;

            NBitcoin.Block genesisBlock = CreateGenesisBlock(consensusFactory, this.GenesisTime, this.GenesisNonce, this.GenesisBits, this.GenesisVersion, this.GenesisReward, "regtestposgenesisblock");

            this.Genesis = genesisBlock;

            // Taken from Stratis.
            var consensusOptions = new PosConsensusOptions(
                maxBlockBaseSize: 1_000_000,
                maxStandardVersion: 2,
                maxStandardTxWeight: 150_000,
                maxBlockSigopsCost: 20_000,
                maxStandardTxSigopsCost: 20_000 / 2,
                witnessScaleFactor: 4
            );

            var buriedDeployments = new BuriedDeploymentsArray
            {
                [BuriedDeployments.BIP34] = 0,
                [BuriedDeployments.BIP65] = 0,
                [BuriedDeployments.BIP66] = 0
            };

            this.EmbeddedContractContainer = new EmbeddedContractContainer(
                this,
                new Dictionary<uint160, EmbeddedContractDescriptor> { },
                new PrimaryAuthenticators(this, new[]
                {
                    "qZc3WCqj8dipxUau1q18rT6EMBN6LRZ44A",
                    "qeEpNUPeRU4f2U9uWDoukbhqKyVrDt8Pn2",
                    "qPwZeTFkTG4kYueCjxQ532EpUYYBFSevzH"
                }, 2));

            // To successfully process the OP_FEDERATION opcode the federations should be known.
            this.Federations = new Federations();

            // Cirrus federation.
            var cirrusFederationMnemonics = new[] {
                "ensure feel swift crucial bridge charge cloud tell hobby twenty people mandate",
                "quiz sunset vote alley draw turkey hill scrap lumber game differ fiction",
                "exchange rent bronze pole post hurry oppose drama eternal voice client state"
               }.Select(m => new Mnemonic(m, Wordlist.English)).ToList();

            // Will replace the last multisig member.
            var newFederationMemberMnemonics = new string[]
            {
                "fat chalk grant major hair possible adjust talent magnet lobster retreat siren"
            }.Select(m => new Mnemonic(m, Wordlist.English)).ToList();

            // Mimic the code found in CirrusRegTest.
            var cirrusFederationKeys = cirrusFederationMnemonics.Take(2).Concat(newFederationMemberMnemonics).Select(m => m.DeriveExtKey().PrivateKey).ToList();

            List<PubKey> cirrusFederationPubKeys = cirrusFederationKeys.Select(k => k.PubKey).ToList();

            // Transaction-signing keys!
            this.Federations.RegisterFederation(new Federation(cirrusFederationPubKeys.ToArray()));

            var bip9Deployments = new NoBIP9Deployments();

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
                minerConfirmationWindow: 144, // Set to a low value for regtest.
                maxReorgLength: 500,
                defaultAssumeValid: null, // turn off assumevalid for regtest.
                maxMoney: long.MaxValue,
                coinbaseMaturity: 10,
                premineHeight: 2,
                premineReward: Money.Coins(130000000),
                proofOfWorkReward: Money.Coins(18),
                powTargetTimespan: TimeSpan.FromSeconds(14 * 24 * 60 * 60), // two weeks
                targetSpacing: TimeSpan.FromSeconds(45),
                powAllowMinDifficultyBlocks: true,
                posNoRetargeting: true,
                powNoRetargeting: true,
                powLimit: powLimit,
                minimumChainWork: null,
                isProofOfStake: true,
                lastPowBlock: 12500,
                proofOfStakeLimit: new BigInteger(uint256.Parse("00000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffff").ToBytes(false)),
                proofOfStakeLimitV2: new BigInteger(uint256.Parse("000000000000ffffffffffffffffffffffffffffffffffffffffffffffffffff").ToBytes(false)),
                proofOfStakeReward: Money.Coins(18)
            );

            this.Consensus.PosEmptyCoinbase = false;

            this.Base58Prefixes = new byte[12][];
            this.Base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { (120) };
            this.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { (127) };
            this.Base58Prefixes[(int)Base58Type.SECRET_KEY] = new byte[] { (120 + 128) };

            // Copied from StraxTest:
            this.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_NO_EC] = new byte[] { 0x01, 0x42 };
            this.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_EC] = new byte[] { 0x01, 0x43 };
            this.Base58Prefixes[(int)Base58Type.EXT_PUBLIC_KEY] = new byte[] { (0x04), (0x88), (0xB2), (0x1E) };
            this.Base58Prefixes[(int)Base58Type.EXT_SECRET_KEY] = new byte[] { (0x04), (0x88), (0xAD), (0xE4) };
            this.Base58Prefixes[(int)Base58Type.PASSPHRASE_CODE] = new byte[] { 0x2C, 0xE9, 0xB3, 0xE1, 0xFF, 0x39, 0xE2 };
            this.Base58Prefixes[(int)Base58Type.CONFIRMATION_CODE] = new byte[] { 0x64, 0x3B, 0xF6, 0xA8, 0x9A };
            this.Base58Prefixes[(int)Base58Type.STEALTH_ADDRESS] = new byte[] { 0x2a };
            this.Base58Prefixes[(int)Base58Type.ASSET_ID] = new byte[] { 23 };
            this.Base58Prefixes[(int)Base58Type.COLORED_ADDRESS] = new byte[] { 0x13 };

            this.Checkpoints = new Dictionary<int, CheckpointInfo>()
            {
            };

            this.Bech32Encoders = new Bech32Encoder[2];
            var encoder = new Bech32Encoder("tstrax");
            this.Bech32Encoders[(int)Bech32Type.WITNESS_PUBKEY_ADDRESS] = encoder;
            this.Bech32Encoders[(int)Bech32Type.WITNESS_SCRIPT_ADDRESS] = encoder;

            this.DNSSeeds = new List<DNSSeedData>();
            this.SeedNodes = new List<NetworkAddress>();

            this.StandardScriptsRegistry = new PoSStandardScriptsRegistry();

            Assert(this.DefaultBanTimeSeconds <= this.Consensus.MaxReorgLength * this.Consensus.TargetSpacing.TotalSeconds / 2);

            // TODO: Update this once the final block is mined
            Assert(this.Consensus.HashGenesisBlock == uint256.Parse("c9b671170aa57064eeeab9bf3964d06962669078197b44ad02e22255488544e2"));

            RegisterRules(this.Consensus);
            RegisterMempoolRules(this.Consensus);
        }


        public static NBitcoin.Block CreateGenesisBlock(ConsensusFactory consensusFactory, uint time, uint nonce, uint bits, int version, Money genesisReward, string genesisText)
        {
            Transaction txNew = consensusFactory.CreateTransaction();
            txNew.Version = 1;
            txNew.AddInput(new TxIn()
            {
                ScriptSig = new Script(Op.GetPushOp(0), new Op()
                {
                    Code = (OpcodeType)0x1,
                    PushData = new[] { (byte)42 }
                }, Op.GetPushOp(Encoders.ASCII.DecodeData(genesisText)))
            });
            txNew.AddOutput(new TxOut()
            {
                Value = genesisReward,
            });

            NBitcoin.Block genesis = consensusFactory.CreateBlock();
            genesis.Header.BlockTime = Utils.UnixTimeToDateTime(time);
            genesis.Header.Bits = bits;
            genesis.Header.Nonce = nonce;
            genesis.Header.Version = version;
            genesis.Transactions.Add(txNew);
            genesis.Header.HashPrevBlock = uint256.Zero;
            genesis.UpdateMerkleRoot();

            /*
            Procedure for creating a new genesis block:
            1. Create the template block as above in the CreateStraxGenesisBlock method

            3. Iterate over the nonce until the proof-of-work is valid
            */

            //while (!genesis.CheckProofOfWork())
            //{
            //   genesis.Header.Nonce++;
            //   if (genesis.Header.Nonce == 0)
            //       genesis.Header.Time++;
            //}

            /*
            4. This will mean the block header hash is under the target
            5. Retrieve the Nonce and Time values from the resulting block header and insert them into the network definition
            */

            return genesis;

        }

        public static void RegisterRules(IConsensus consensus)
        {
            consensus.ConsensusRules
                .Register<HeaderTimeChecksRule>()
                .Register<HeaderTimeChecksPosRule>()
                .Register<PosFutureDriftRule>()
                .Register<CheckDifficultyPosRule>()
                .Register<StratisHeaderVersionRule>()
                .Register<ProvenHeaderSizeRule>()
                .Register<ProvenHeaderCoinstakeRule>();

            consensus.ConsensusRules
                .Register<BlockMerkleRootRule>()
                .Register<PosBlockSignatureRepresentationRule>()
                .Register<PosBlockSignatureRule>();

            consensus.ConsensusRules
                .Register<SetActivationDeploymentsPartialValidationRule>()
                .Register<PosTimeMaskRule>()

                // rules that are inside the method ContextualCheckBlock
                .Register<TransactionLocktimeActivationRule>()
                .Register<CoinbaseHeightActivationRule>()
                .Register<WitnessCommitmentsRule>()
                .Register<BlockSizeRule>()

                // rules that are inside the method CheckBlock
                .Register<EnsureCoinbaseRule>()
                .Register<CheckPowTransactionRule>()
                .Register<CheckPosTransactionRule>()
                .Register<CheckSigOpsRule>()
                .Register<StraxCoinstakeRule>();

            consensus.ConsensusRules
                .Register<SetActivationDeploymentsFullValidationRule>()

                .Register<CheckDifficultyHybridRule>()

                // rules that require the store to be loaded (coinview)
                .Register<LoadCoinviewRule>()
                .Register<TransactionDuplicationActivationRule>()

                // Smart contract specific
                .Register<ContractTransactionFullValidationRule>()
                .Register<TxOutSmartContractExecRule>()
                .Register<OpSpendRule>()
                .Register<CanGetSenderRule>()
                .Register<P2PKHNotContractRule>()

                .Register<StraxCoinviewRule>() // implements BIP68, MaxSigOps and BlockReward calculation
                                               // Place the PosColdStakingRule after the PosCoinviewRule to ensure that all input scripts have been evaluated
                                               // and that the "IsColdCoinStake" flag would have been set by the OP_CHECKCOLDSTAKEVERIFY opcode if applicable.
                .Register<StraxColdStakingRule>()
                .Register<SaveCoinviewRule>();
        }

        public static void RegisterMempoolRules(IConsensus consensus)
        {
            consensus.MempoolRules = new List<Type>()
            {
                typeof(CheckConflictsMempoolRule),
                typeof(StraxCoinViewMempoolRule),
                typeof(CreateMempoolEntryMempoolRule),
                typeof(CheckSigOpsMempoolRule),
                typeof(StraxTransactionFeeMempoolRule),

                // The smart contract mempool needs to do more fee checks than its counterpart, so include extra rules.
                // These rules occur directly after the fee check rule in the non- smart contract mempool.
                typeof(SmartContractFormatLogicMempoolRule),
                typeof(CanGetSenderMempoolRule),
                typeof(AllowedCodeHashLogicMempoolRule),
                typeof(CheckMinGasLimitSmartContractMempoolRule),

                typeof(CheckRateLimitMempoolRule),
                typeof(CheckAncestorsMempoolRule),
                typeof(CheckReplacementMempoolRule),
                typeof(CheckAllInputsMempoolRule),
                typeof(CheckTxOutDustRule)
            };
        }
    }

    /// <summary>
    /// Strax-specific standard transaction definitions.
    /// </summary>
    public class PoSStandardScriptsRegistry : StandardScriptsRegistry
    {
        public const int MaxOpReturnRelay = 83;

        // Need a network-specific version of the template list
        private static readonly List<ScriptTemplate> standardTemplates = new List<ScriptTemplate>
        {
            new PayToPubkeyHashTemplate(),
            new PayToPubkeyTemplate(),
            new PayToScriptHashTemplate(),
            new PayToMultiSigTemplate(),
            new PayToFederationTemplate(),
            new TxNullDataTemplate(MaxOpReturnRelay),
            new PayToWitTemplate()
        };

        public override List<ScriptTemplate> GetScriptTemplates => standardTemplates;
    }
}
