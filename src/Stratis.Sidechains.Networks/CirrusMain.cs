using System;
using System.Collections.Generic;
using System.Net;
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
    /// <see cref="PoANetwork"/>.
    /// </summary>
    public class CirrusMain : PoANetwork
    {
        public CirrusMain()
        {
            this.Name = "CirrusMain";
            this.NetworkType = NetworkType.Mainnet;
            this.CoinTicker = "CRS";
            this.Magic = 0x522357AC;
            this.DefaultPort = 16179;
            this.DefaultMaxOutboundConnections = 16;
            this.DefaultMaxInboundConnections = 109;
            this.DefaultRPCPort = 16175;
            this.DefaultAPIPort = 37223;
            this.DefaultSignalRPort = 38823;
            this.MaxTipAge = 768; // 20% of the fastest time it takes for one MaxReorgLength of blocks to be mined.
            this.MinTxFee = 10000;
            this.FallbackFee = 10000;
            this.MinRelayTxFee = 10000;
            this.RootFolderName = CirrusNetwork.NetworkRootFolderName;
            this.DefaultConfigFilename = CirrusNetwork.NetworkDefaultConfigFilename;
            this.MaxTimeOffsetSeconds = 25 * 60;
            this.DefaultBanTimeSeconds = 1920; // 240 (MaxReorg) * 16 (TargetSpacing) / 2 = 32 Minutes

            this.CirrusRewardDummyAddress = "CPqxvnzfXngDi75xBJKqi4e6YrFsinrJka";

            this.ConversionTransactionFeeDistributionDummyAddress = "CXK1AhmK8XhmBWHUrCKRt5WMhz1CcYeguF";

            var consensusFactory = new SmartContractCollateralPoAConsensusFactory();

            // Create the genesis block.
            this.GenesisTime = 1561982325;
            this.GenesisNonce = 3038481;
            this.GenesisBits = new Target(new uint256("00000fffff000000000000000000000000000000000000000000000000000000"));
            this.GenesisVersion = 1;
            this.GenesisReward = Money.Zero;

            string coinbaseText = "https://github.com/stratisproject/StratisBitcoinFullNode";
            Block genesisBlock = CirrusNetwork.CreateGenesis(consensusFactory, this.GenesisTime, this.GenesisNonce, this.GenesisBits, this.GenesisVersion, this.GenesisReward, coinbaseText);

            this.Genesis = genesisBlock;

            // Configure federation public keys used to sign blocks.
            // Keep in mind that order in which keys are added to this list is important
            // and should be the same for all nodes operating on this network.
            var genesisFederationMembers = new List<IFederationMember>()
            {
                new CollateralFederationMember(new PubKey("03f5de5176e29e1e7d518ae76c1e020b1da18b57a3713ac81b16015026e232748e"), true, new Money(50000_00000000),"XVm4UgRVnez2vkcJ8r3vMePL3iP2kbDvUk"),
                new CollateralFederationMember(new PubKey("021043aacac5c8805e3bc62eb40e8d3c04070c56b21032d4bb14200ed6e4facf93"), true, new Money(50000_00000000),"ScrS22tPNxL2q1Q8u9bFPX29WwWfnmTZJ6"),
                new CollateralFederationMember(new PubKey("0323033679aa439a0388f09f2883bf1ca6f50283b41bfeb6be6ddcc4e420144c16"), true, new Money(50000_00000000),"XZFyoh3jXeyMPFqmhDYLeUNz7cWxjzzTw5"),
                new CollateralFederationMember(new PubKey("037b5f0a88a477d9fba812826a3bf43104ca078fc51b62c0eaad15d0f9a724a4b2"), true, new Money(50000_00000000),"SeHbzFEC1CXco4TKTKkBbsfFMBhDyDm8Qa"),
                new CollateralFederationMember(new PubKey("027e793fbf4f6d07de15b0aa8355f88759b8bdf92a9ffb8a65a87fa8ee03baeccd"), true, new Money(50000_00000000),"XJQ6ccf6D82ThpFzg8SnuubCeXaAfQGZhe"),
                new CollateralFederationMember(new PubKey("028e1d9fd64b84a2ec85fac7185deb2c87cc0dd97270cf2d8adc3aa766dde975a7"), true, new Money(50000_00000000),"XFLQqZSE193fTUR1T8F33LaSR2J3wn5UFV"),
                new CollateralFederationMember(new PubKey("03535a285d0919a9bd71df3b274cecb46e16b78bf50d3bf8b0a3b41028cf8a842d"), true, new Money(50000_00000000),"XX5ZeKZNWUJuwucHaHMgfffs6NmSrrPS1p"),
                new CollateralFederationMember(new PubKey("0200c70e46cd94012caaae3fcc124e5f280f63a29cd2b3e15c15bac9d371da1e0d"), true, new Money(50000_00000000),"SkWeFZGkD71qsQF6hPbgMUz4v53JP3FfMo"),
                new CollateralFederationMember(new PubKey("03eb5db0b1703ea7418f0ad20582bf8de0b4105887d232c7724f43f19f14862488"), true, new Money(50000_00000000),"XQk7ZBn6NMaWXJvPDbLVTPPenfHASdcctp"),
                new CollateralFederationMember(new PubKey("03d8b5580b7ec709c006ef497327db27ea323bd358ca45412171c644214483b74f"), true, new Money(50000_00000000),"XDEejrTBQ9reDaaxTqBi5qP5tPWab7p5QH"),
                new CollateralFederationMember(new PubKey("02ace4fbe6a622cdfc922a447c3253e8635f3fecb69241f73629e6f0596a567907"), true, new Money(50000_00000000),"XWpXbf4MsToxeACKpcLNBN7rZkbkUg7P4n"),
                new CollateralFederationMember(new PubKey("03e8809be396745434ee8c875089e518a3eef40e31ade81869ce9cbef63484996d"), true, new Money(50000_00000000),"XZPrRnJKqfQBWGA2TN7qfmpPDPy1rvrwyP"),
                new CollateralFederationMember(new PubKey("03a37019d2e010b046ef9d0459e4844a015758007602ddfbdc9702534924a23695"), true, new Money(50000_00000000),"XFUjiusiLPxbQ7yJpFCPxXqb8XhtXYyRpQ"),
                new CollateralFederationMember(new PubKey("0336312e7dce4f9ff8449a5d7d140be26eea7849f8ba13bb07b57b154a74aa7600"), true, new Money(50000_00000000),"SUMMi8UuoEUEVc5ecr9TEBaKpf152oNz4M"),
                new CollateralFederationMember(new PubKey("038e1a76f0e33474144b61e0796404821a5150c00b05aad8a1cd502c865d8b5b92"), true, new Money(50000_00000000),"XWmTHVNs7eVW1BKrkfL5isdhzctKi12nw6"),
                new CollateralFederationMember(new PubKey("0306441cb6eb5fcd36a6af2972804382f2dc601150f6ecb773f988c3a1b1eea778"), false, new Money(10000_00000000),"SXhWwe72GTj8c2peaLRvqfJq9Ew2GA6wgY"),
                new CollateralFederationMember(new PubKey("02dfd2c5502c2d9fef90ec80c7912588900fb3626d46473b842a9e82ac28649991"), false, new Money(10000_00000000),"XErgowdmpKJvcB2hiLYZwehoYLZyyyhfTE"),
                new CollateralFederationMember(new PubKey("038670251efd386121d3110716addb73fa452fa2891cb88ac14417682366358673"), false, new Money(10000_00000000),"XEn9MArTk8WWUWDfJSZ5UQqvuestd3o8L6"),
                new CollateralFederationMember(new PubKey("02e96ce15caea22e6a38a8c2b06a788f8ac28453ebb77a6578d5f394296cbc8ed4"), false, new Money(10000_00000000),"XXEHUS8dDHtn7M8AcrgweVcozweJcGGy4i"),
                new CollateralFederationMember(new PubKey("02b80af8dc4b20865c79228c53af6365bec92960ffdf2b2f56d7bf0555a05f647a"), false, new Money(10000_00000000),"XMMkSwftUydeKeXPBHEAzYeHpwyVyzZ9cs"),
                new CollateralFederationMember(new PubKey("03edf8ad7419fd7223d5309ee3cfb27f2d4e6a5cd5da80aa3d225e818e7d21b9e6"), false, new Money(10000_00000000),"XNTSvNPB8oYtDtFbNMzTtT9osiYHyF2No2"),
                new CollateralFederationMember(new PubKey("02674553d81d3dbcb6def93026d69bb44f738156223c342a41bda4df1503daec11"), false, new Money(10000_00000000),"SWN51wwcCLnpBZksXeqdP4iMkWHDKERznQ"),
                new CollateralFederationMember(new PubKey("032768540dabcbe8a78fc2916c17a07fecc51647d353e6af22a6daa3281e2d3a70"), false, new Money(10000_00000000),"XEU4jWcnKBnip2pjwdMngKvCf5YqWkt2sz"),
                new CollateralFederationMember(new PubKey("02f40bd4f662ba20629a104115f0ac9ee5eab695716edfe01b240abf56e05797e2"), false, new Money(50000_00000000),"XYR8ZsfDczLptdWjU11xsYEuhBwdUgouGx"),
                new CollateralFederationMember(new PubKey("03dc030fa1c3d19ce5d464bc58440dc54f4905b766ce510e1237d906dff71c081b"), false, new Money(10000_00000000),"SQsYGYrCYdCPpcrwNva4m5GQ1PTdJipQ4d"),
                new CollateralFederationMember(new PubKey("03a620f0ba4f197b53ba3e8591126b54bd728ecc961607221190abb8e3cd91ea5f"), false, new Money(10000_00000000),"XSBx1AUzG8CV3oCHqdkR5P4QuAjm4Sfi3o"),
                new CollateralFederationMember(new PubKey("0247e8dba42a4055f73598a57eddffb2c4db33699f258f529f1762ea29b8cc21a7"), false, new Money(10000_00000000),"XTdqzYt8m4jrpKAvtdcn5McBohn6Hm2nWb"),
                new CollateralFederationMember(new PubKey("029925bc527cec3592973e79b340768231ef6f220d422b1839a6c441ffa1912c1c"), false, new Money(10000_00000000),"XP7snHdXUnnH2bV7KuRqDdzm6Aw1n8PaZe"),
                new CollateralFederationMember(new PubKey("0300cda1f0d37683fc1441cdb8ed0f18190bc56c3f786116a127d3f03369f44b07"), false, new Money(10000_00000000),"SMs2EZssggQ5BcSuTmYgoXvhrNh1jJhHv2"),
                new CollateralFederationMember(new PubKey("0242c518c00b6890f14e0852cc039084fdca84fa5e9563b5d57ec150262b4dcb6c"), false, new Money(10000_00000000),"SX7YZNPNiD77pR9samtZszpgRQutyL7duH"),
                new CollateralFederationMember(new PubKey("02c0dec04c7ccc57c201b5f2e1db22bf4fce6c06be99dc7fec67190115208e835e"), false, new Money(10000_00000000),"SQzdzSufg7sQiFFHu9EG4YujaX9Jt8kE39"),
                new CollateralFederationMember(new PubKey("0204cc7a01d4423a83081b6711c1e93a38ec9ff115331da933ae59937d5c075ca3"), false, new Money(10000_00000000),"XNGRmg6ofPyuV9VPQjZFUcvRMBSRnSYDa9"),
                new CollateralFederationMember(new PubKey("0317abe6a28cc7af44a46de97e7c6120c1ccec78afb83efe18030f5c36e3016b32"), false, new Money(50000_00000000),"XJoH64HZEK9YfRdpPToXMGU7fsscYWmWhP"),
                new CollateralFederationMember(new PubKey("03a75ed5b0cfe69957551d929492a5d7847b47c71de4a2c95c1036177c9294b9c3"), false, new Money(10000_00000000),"SSd2RbVC6nahmTQc7kaN9FUq2RCoEBkGuK"),
                new CollateralFederationMember(new PubKey("02b7af1d3e27ec3758bb59926ca3809013d6cd869808f4fae6d0426ce3166c6af2"), false, new Money(10000_00000000),"XFxsW9B6wHMuTVebbWqvtk4PXwfrag316U"),
                new CollateralFederationMember(new PubKey("03d621e270932fd41a29d9658384eb75bf00416b5b8351228f4653a06f4c942b68"), false, new Money(10000_00000000),"XUUByGnhCMPC36mCsN9psyzaULeHhqTQNy"),
                new CollateralFederationMember(new PubKey("036a88ab8b860ecd00e6b35e3e04d353a2dd60937abc0a0d0e483220c1e95e51fc"), false, new Money(10000_00000000),"STxDmPYCxq3MEmtoYGk8oLRG1ujWe5FX3p"),
                new CollateralFederationMember(new PubKey("031eaad893aa056059c606ea9d4b2d2f21cdcb75ad1f4182dcc6d486ad2d3482c1"), false, new Money(10000_00000000),"Sj424EfSHG7WxRPxp2gBMfXqE3Wj6h3ZWz"),
                new CollateralFederationMember(new PubKey("025cb67811d0922ca77fa33f19c3e5c37961f9639a1f0a116011b9075f6796abcb"), false, new Money(50000_00000000),"XBNRUeYXf7iREhtuxddX7gEpUyZjp857gj"),
                new CollateralFederationMember(new PubKey("036437789fac0ab74cda93d98b519c28608a48ef86c3bd5e8227af606c1e025f61"), false, new Money(50000_00000000),"XPzvwkN8Z6ERjmpJHKQUwGEbXQ4nJTJb8w"),
                new CollateralFederationMember(new PubKey("024ca136db3fd5f72e30ff91cbbdf9ab7a0a1da186b3fc7ad5f861a4742fa42cdd"), false, new Money(10000_00000000),"XQLFJ5uGcCxnAzA2TbhSvf5a3XgRcZX4Cg"),
                new CollateralFederationMember(new PubKey("02a523078d5391f69ad3ee1554cf4afad3ce4c0946ff92c7447e5b7c7197967314"), false, new Money(10000_00000000),"SaZ8oZAasmSp5kJRnGx1aPDW5nqSjBxR7z"),
                new CollateralFederationMember(new PubKey("02d57eaa61845c5ce07963b211af83c3fe072a9de65c555f7bdbd7c38efe65e42a"), false, new Money(10000_00000000),"XGfxyrFrbAMP72bSgtW3jww4ZcphKe4Yzp"),
                new CollateralFederationMember(new PubKey("0371c8558c846172eaf694a4e3af4d6cfdbfdd0d8480666c206ea43522c65a926a"), false, new Money(10000_00000000),"SREEeESBB1fiSCEfZ7qDBuQeZtM7byCyoG"),
                new CollateralFederationMember(new PubKey("03adce7b60c2a3b03f9567d44bcf4e1d98200a736914a4385a4ef8c248d50b71ba"), false, new Money(10000_00000000),"XMcSvUpa7JBABg16ugFKWJWpZMxpwM6JwS"),
                new CollateralFederationMember(new PubKey("028bbb6d3eca487640fab54c5800beb9e9d0f20c072805f08f0a4ae2af8bec596d"), false, new Money(10000_00000000),"SUGnHfLwuCidT3mRR6i8ZrNgYHPjBbdUzJ")
            };

            this.Federations = new Federations();
            var straxFederationTransactionSigningKeys = new List<PubKey>()
            {
                new PubKey("03797a2047f84ba7dcdd2816d4feba45ae70a59b3aa97f46f7877df61aa9f06a21"),
                new PubKey("0209cfca2490dec022f097114090c919e85047de0790c1c97451e0f50c2199a957"),
                new PubKey("032e4088451c5a7952fb6a862cdad27ea18b2e12bccb718f13c9fdcc1caf0535b4"),
                new PubKey("035bf78614171397b080c5b375dbb7a5ed2a4e6fb43a69083267c880f66de5a4f9"),
                new PubKey("02387a219b1de54d4dc73a710a2315d957fc37ab04052a6e225c89205b90a881cd"),
                new PubKey("028078c0613033e5b4d4745300ede15d87ed339e379daadc6481d87abcb78732fa"),
                new PubKey("02b3e16d2e4bbad6dba1e699934a52d58d9b60b6e7eed303e400e95f2dbc2ef3fd"),
                new PubKey("02ba8b842997ce50c8e29c24a5452de5482f1584ae79778950b7bae24d4cc68dad"),
                new PubKey("02cbd907b0bf4d757dee7ea4c28e63e46af19dc8df0c924ee5570d9457be2f4c73"),
                new PubKey("02d371f3a0cffffcf5636e6d4b79d9f018a1a18fbf64c39542b382c622b19af9de"),
                new PubKey("02f891910d28fc26f272da8d7f548fdc18c286704907673e839dc07e8df416c15e"),
                new PubKey("0337e816a3433c71c4bbc095a54a0715a6da7a70526d2afb8dba3d8d78d33053bf"),
                new PubKey("035569e42835e25c854daa7de77c20f1009119a5667494664a46b5154db7ee768a"),
                new PubKey("03cda7ea577e8fbe5d45b851910ec4a795e5cc12d498cf80d39ba1d9a455942188"),
                new PubKey("02680321118bce869933b07ea42cc04d2a2804134b06db582427d6b9688b3536a4")
            };

            // Register the new set of federation members.
            this.Federations.RegisterFederation(new Federation(straxFederationTransactionSigningKeys));

            // The height at which the following list of members apply.
            this.MultisigMinersApplicabilityHeight = 1413998;

            // Set the list of Strax Era mining keys.
            this.StraxMiningMultisigMembers = new List<PubKey>()
            {
                new PubKey("02ace4fbe6a622cdfc922a447c3253e8635f3fecb69241f73629e6f0596a567907"),
                new PubKey("028e1d9fd64b84a2ec85fac7185deb2c87cc0dd97270cf2d8adc3aa766dde975a7"),
                new PubKey("025cb67811d0922ca77fa33f19c3e5c37961f9639a1f0a116011b9075f6796abcb"),
                new PubKey("027e793fbf4f6d07de15b0aa8355f88759b8bdf92a9ffb8a65a87fa8ee03baeccd"),
                new PubKey("03eb5db0b1703ea7418f0ad20582bf8de0b4105887d232c7724f43f19f14862488"),
                new PubKey("03e8809be396745434ee8c875089e518a3eef40e31ade81869ce9cbef63484996d"),
                new PubKey("0317abe6a28cc7af44a46de97e7c6120c1ccec78afb83efe18030f5c36e3016b32"),
                new PubKey("038e1a76f0e33474144b61e0796404821a5150c00b05aad8a1cd502c865d8b5b92"),
                new PubKey("036437789fac0ab74cda93d98b519c28608a48ef86c3bd5e8227af606c1e025f61"),
                new PubKey("03d8b5580b7ec709c006ef497327db27ea323bd358ca45412171c644214483b74f"),
                new PubKey("02f40bd4f662ba20629a104115f0ac9ee5eab695716edfe01b240abf56e05797e2"),
                new PubKey("0323033679aa439a0388f09f2883bf1ca6f50283b41bfeb6be6ddcc4e420144c16"),
                new PubKey("03535a285d0919a9bd71df3b274cecb46e16b78bf50d3bf8b0a3b41028cf8a842d"),
                new PubKey("03a37019d2e010b046ef9d0459e4844a015758007602ddfbdc9702534924a23695"),
                new PubKey("03f5de5176e29e1e7d518ae76c1e020b1da18b57a3713ac81b16015026e232748e"),
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
                federationMemberMaxIdleTimeSeconds: 60 * 60 * 24 * 2 // 2 days
            )
            {
                EnforceMinProtocolVersionAtBlockHeight = 384675, // setting the value to zero makes the functionality inactive
                EnforcedMinProtocolVersion = ProtocolVersion.CIRRUS_VERSION, // minimum protocol version which will be enforced at block height defined in EnforceMinProtocolVersionAtBlockHeight
                FederationMemberActivationTime = 1605862800, // Friday, November 20, 2020 9:00:00 AM
                InterFluxV2MainChainActivationHeight = 460_000,
                VotingManagerV2ActivationHeight = 1_683_000, // Tuesday, 12 January 2021 9:00:00 AM (Estimated)
                Release1100ActivationHeight = 3_200_000,
                PollExpiryBlocks = 50_000 // Roughly 9 days
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
                coinType: 401,
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
                defaultAssumeValid: new uint256("0xbfd4a96a6c5250f18bf7c586761256fa5f8753ffa10b24160f0648a452823a95"), // 1400000
                maxMoney: Money.Coins(100_000_000),
                coinbaseMaturity: 1,
                premineHeight: 2,
                premineReward: Money.Coins(100_000_000),
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
            this.Base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { 28 }; // C
            this.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { 88 }; // c
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
                { 50000, new CheckpointInfo(new uint256("0xf3ed37db1c56751fdf9f45902696dd034444a697cd8c106a08f4c60cd2de9d77")) },
                { 100000, new CheckpointInfo(new uint256("0x1400cb20800d54cd7fff5fea90133a1a8ca44e7043268cd0c7efdd7f8186b2d0")) },
                { 150000, new CheckpointInfo(new uint256("0x505d22805f0fc4ea057edad778e7334412526a7c1b017b179be5d274c8d42914")) },
                { 200000, new CheckpointInfo(new uint256("0x5569221c600e42b0467c92bd932046c12198eee5c50ac98eadff7d3159f55b75")) },
                { 250000, new CheckpointInfo(new uint256("0x1a0d5f43335eff00e8a3b5dc09e4f6849b571b6870eb58364cf86623222922d7")) },
                { 300000, new CheckpointInfo(new uint256("0x3b1c3704e0cb79e7fff46ab7e9feacbfa9e2e95ab90b273d99520dbd42cc34b6")) },
                { 350000, new CheckpointInfo(new uint256("0xcb420b8ef20e1da9eb63b6847005b17928b4bad6c2920eebc964ecf21c50ce5a")) },
                { 400000, new CheckpointInfo(new uint256("0xa501a5c69dfce78e39bf0c25d2c1eafa9fd7a9f32ee06b419d3a3c0a6ac29d8b")) },
                { 450000, new CheckpointInfo(new uint256("0xc3ae6119d23294ac51c05f9c761da5271711b1945592cb83cc1bcc1b908780c7")) },
                { 500000, new CheckpointInfo(new uint256("0x810cc011d6d5158aaefcc38550a31b4118fae1bb18ea7894f81a2edc81126d5f")) },
                { 550000, new CheckpointInfo(new uint256("0x3a6b0a58deb1997879d35fc6e017123594c00eafb3ac45d8c31a5dbf68c2bccc")) },
                { 600000, new CheckpointInfo(new uint256("0xc79bf7066ec9243a335fcd2a43380a47a5b9dccdeaee3f67ab5503cef0cd1626")) },
                { 700000, new CheckpointInfo(new uint256("0xe777ae5e283564a994cbcf88315a594854c12d626e6908fb27e3d0cd7d04fcc7")) },
                { 800000, new CheckpointInfo(new uint256("0xe8b2b9b4e342b0ff9a0b1b967b0f2b7481fe420c5922322d1b77cfae66471fa1")) },
                { 900000, new CheckpointInfo(new uint256("0x30599fbbce4404ebaff9f8d0ea7071c684f124439f1f4e9fabec0debad6c7a06")) },
                { 1_000_000, new CheckpointInfo(new uint256("0x547faf99acb45e2195ea5fbb6873562c44a7696f6571e8a309d6c9f509be064a")) },
                { 1_100_000, new CheckpointInfo(new uint256("0x7abc2882bcb5e9723ba71ff4155ed3c4006ee655e9f52f8787bcae31b4c796a8")) },
                { 1_200_000, new CheckpointInfo(new uint256("0x8411b830270cc9d6c2e28de1c2e8025c57a5673835f63e30708967adfee5a92c")) },
                { 1_300_000, new CheckpointInfo(new uint256("0x512c19a8245316b4d3b13513c7901f41842846f539f668ca4ac349daaab6dc20")) },
                { 1_400_000, new CheckpointInfo(new uint256("0xbfd4a96a6c5250f18bf7c586761256fa5f8753ffa10b24160f0648a452823a95")) },
                { 1_500_000, new CheckpointInfo(new uint256("0x2a1602877a5231997654bae975223762ee636be2f371cb444b2d3fb564e6989e")) },
                { 1_750_000, new CheckpointInfo(new uint256("0x58c96a878efeeffea1b1924b61eed627687900e01588ffaa2f4a161973f01abf")) },
                { 1_850_000, new CheckpointInfo(new uint256("0x6e2590bd9a8eaab25b236c0c9ac314abec70b18aa053b96c9257f2356dec8314")) },
                { 2_150_000, new CheckpointInfo(new uint256("0x4c65f29b5098479cab275afd77d302ebe5ed8d8ef33e02ae54bf185865763f18")) },
                { 2_500_000, new CheckpointInfo(new uint256("0x2853be7b7224840d3d4b60427ea832e9bd67d8fc6bfcd4956b8c6b2414cf8fc2")) },
                { 2_827_550, new CheckpointInfo(new uint256("0xcf0ebdd99ec04ef260d22befe70ef7b948e50b5fcc18d9d37376d49e872372a0")) }
            };

            this.DNSSeeds = new List<DNSSeedData>
            {
                new DNSSeedData("cirrusmain1.stratisnetwork.com", "cirrusmain1.stratisnetwork.com")
            };

            this.SeedNodes = new List<NetworkAddress>
            {
                new NetworkAddress(IPAddress.Parse("213.125.242.234"), 16179),
                new NetworkAddress(IPAddress.Parse("45.58.55.21"), 16179),
                new NetworkAddress(IPAddress.Parse("86.106.181.141"), 16179),
                new NetworkAddress(IPAddress.Parse("51.195.136.221"), 16179)
            };

            this.StandardScriptsRegistry = new PoAStandardScriptsRegistry();

            Assert(this.DefaultBanTimeSeconds <= this.Consensus.MaxReorgLength * this.Consensus.TargetSpacing.TotalSeconds / 2);
            Assert(this.Consensus.HashGenesisBlock == uint256.Parse("000005769503496300ec879afd7543dc9f86d3b3d679950b2b83e2f49f525856"));
            Assert(this.Genesis.Header.HashMerkleRoot == uint256.Parse("1669a55d45b642af0ce82c5884cf5b8d8efd5bdcb9a450c95f442b9bd1ff65ea"));

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
