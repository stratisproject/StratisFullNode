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
    public sealed class StraxTest : StraxMain
    {
        public StraxTest()
        {
            this.Name = "StraxTest";
            this.NetworkType = NetworkType.Testnet;
            this.Magic = BitConverter.ToUInt32(Encoding.ASCII.GetBytes("TtrX"));
            this.DefaultPort = 17100;
            this.DefaultMaxOutboundConnections = 16;
            this.DefaultMaxInboundConnections = 100;
            this.DefaultRPCPort = 18100;
            this.DefaultAPIPort = 37100;
            this.DefaultSignalRPort = 38100;
            this.MaxTipAge = 2 * 60 * 60;
            this.MinTxFee = 10000;
            this.FallbackFee = 10000;
            this.MinRelayTxFee = 10000;
            this.RootFolderName = StraxNetworkConstants.StraxRootFolderName;
            this.DefaultConfigFilename = StraxNetworkConstants.StraxDefaultConfigFilename;
            this.MaxTimeOffsetSeconds = 25 * 60;
            this.CoinTicker = "TSTRAX";
            this.DefaultBanTimeSeconds = 16000; // 500 (MaxReorg) * 64 (TargetSpacing) / 2 = 4 hours, 26 minutes and 40 seconds

            var consensusFactory = new PosConsensusFactory();

            // Create the genesis block.
            this.GenesisTime = 1598918400; // 1 September 2020
            this.GenesisNonce = 1831645; // TODO: Check
            this.GenesisBits = 0x1e0fffff; // TODO: Check
            this.GenesisVersion = 1;
            this.GenesisReward = Money.Zero;

            Block genesisBlock = CreateStraxGenesisBlock(consensusFactory, this.GenesisTime, this.GenesisNonce, this.GenesisBits, this.GenesisVersion, this.GenesisReward);

            this.Genesis = genesisBlock;

            // Taken from Stratis.
            var consensusOptions = new PosConsensusOptions(
                maxBlockBaseSize: 1_000_000,
                maxStandardVersion: 2,
                maxStandardTxWeight: 100_000,
                maxBlockSigopsCost: 20_000,
                maxStandardTxSigopsCost: 20_000 / 5
            );

            var buriedDeployments = new BuriedDeploymentsArray
            {
                [BuriedDeployments.BIP34] = 0,
                [BuriedDeployments.BIP65] = 0,
                [BuriedDeployments.BIP66] = 0
            };

            var bip9Deployments = new StratisBIP9Deployments()
            {
                // TODO: Add the BIP9 deployments after segwit is merged (cold staking, segwit, csv)
            };

            this.Consensus = new NBitcoin.Consensus(
                consensusFactory: consensusFactory,
                consensusOptions: consensusOptions,
                coinType: 200,
                hashGenesisBlock: genesisBlock.GetHash(),
                subsidyHalvingInterval: 210000,
                majorityEnforceBlockUpgrade: 750,
                majorityRejectBlockOutdated: 950,
                majorityWindow: 1000,
                buriedDeployments: buriedDeployments,
                bip9Deployments: bip9Deployments,
                bip34Hash: null, // TODO: Check
                minerConfirmationWindow: 2016,
                maxReorgLength: 500,
                defaultAssumeValid: null,// TODO: Check
                maxMoney: long.MaxValue,
                coinbaseMaturity: 50,
                premineHeight: 2,
                premineReward: Money.Coins(98000000), // TODO: Check
                proofOfWorkReward: Money.Coins(4), // TODO: Check
                powTargetTimespan: TimeSpan.FromSeconds(14 * 24 * 60 * 60),
                powTargetSpacing: TimeSpan.FromSeconds(10 * 60),
                powAllowMinDifficultyBlocks: false,
                posNoRetargeting: false,
                powNoRetargeting: false,
                powLimit: new Target(new uint256("00000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")),
                minimumChainWork: null,
                isProofOfStake: true,
                lastPowBlock: 12500,
                proofOfStakeLimit: new BigInteger(uint256.Parse("00000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffff").ToBytes(false)),
                proofOfStakeLimitV2: new BigInteger(uint256.Parse("000000000000ffffffffffffffffffffffffffffffffffffffffffffffffffff").ToBytes(false)),
                proofOfStakeReward: Money.COIN // TODO: Check
            );

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

            this.Bech32Encoders = new Bech32Encoder[2]; // TODO: Check
            // Bech32 is currently unsupported on Stratis - once supported uncomment lines below
            //var encoder = new Bech32Encoder("bc");
            //this.Bech32Encoders[(int)Bech32Type.WITNESS_PUBKEY_ADDRESS] = encoder;
            //this.Bech32Encoders[(int)Bech32Type.WITNESS_SCRIPT_ADDRESS] = encoder;
            this.Bech32Encoders[(int)Bech32Type.WITNESS_PUBKEY_ADDRESS] = null;
            this.Bech32Encoders[(int)Bech32Type.WITNESS_SCRIPT_ADDRESS] = null;

            this.DNSSeeds = new List<DNSSeedData>
            {
            };

            this.SeedNodes = new List<NetworkAddress>
            {
            };

            this.StandardScriptsRegistry = new StratisStandardScriptsRegistry();

            // 64 below should be changed to TargetSpacingSeconds when we move that field.
            Assert(this.DefaultBanTimeSeconds <= this.Consensus.MaxReorgLength * 64 / 2);

            // TODO: Check
            //Assert(this.Consensus.HashGenesisBlock == uint256.Parse("0x0000066e91e46e5a264d42c89e1204963b2ee6be230b443e9159020539d972af"));
            //Assert(this.Genesis.Header.HashMerkleRoot == uint256.Parse("0x65a26bc20b0351aebf05829daefa8f7db2f800623439f3c114257c91447f1518"));

            this.RegisterRules(this.Consensus);
            this.RegisterMempoolRules(this.Consensus);
        }

        private Block CreateStraxGenesisBlock(ConsensusFactory consensusFactory, uint time, uint nonce, uint bits, int version, Money genesisReward)
        {
            Transaction txNew = consensusFactory.CreateTransaction();
            txNew.Version = 1;
            txNew.Time = time;
            txNew.AddInput(new TxIn()
            {
                ScriptSig = new Script(Op.GetPushOp(0), new Op()
                {
                    Code = (OpcodeType)0x1,
                    PushData = new[] { (byte)42 }
                }, Op.GetPushOp(Encoders.ASCII.DecodeData("teststraxgenesisblock")))
            });
            txNew.AddOutput(new TxOut()
            {
                Value = genesisReward,
            });

            Block genesis = consensusFactory.CreateBlock();
            genesis.Header.BlockTime = Utils.UnixTimeToDateTime(time);
            genesis.Header.Bits = bits;
            genesis.Header.Nonce = nonce;
            genesis.Header.Version = version;
            genesis.Transactions.Add(txNew);
            genesis.Header.HashPrevBlock = uint256.Zero;
            genesis.UpdateMerkleRoot();

            return genesis;
        }
    }
}
