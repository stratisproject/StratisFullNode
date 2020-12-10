using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.PoA.BasePoAFeatureConsensusRules;
using Xunit;

namespace Stratis.Bitcoin.Features.PoA.Tests.Rules
{
    public sealed class PoAHeaderSignatureRuleTests : PoATestsBase
    {
        private readonly PoAHeaderSignatureRule signatureRule;

        private static readonly Key key = new KeyTool(new DataFolder(string.Empty)).GeneratePrivateKey();

        public PoAHeaderSignatureRuleTests() : base(new TestPoANetwork(new List<PubKey>() { key.PubKey }))
        {
            this.signatureRule = new PoAHeaderSignatureRule();
            this.InitRule(this.signatureRule);
        }

        [Fact]
        public async Task SignatureIsValidatedAsync()
        {
            var validationContext = new ValidationContext() { ChainedHeaderToValidate = this.currentHeader };
            var ruleContext = new RuleContext(validationContext, DateTimeOffset.Now);

            Key randomKey = new KeyTool(new DataFolder(string.Empty)).GeneratePrivateKey();
            this.poaHeaderValidator.Sign(randomKey, this.currentHeader.Header as PoABlockHeader);

            this.chainState.ConsensusTip = new ChainedHeader(this.network.GetGenesis().Header, this.network.GetGenesis().GetHash(), 0);

            Assert.Throws<ConsensusErrorException>(() => this.signatureRule.RunAsync(ruleContext).GetAwaiter().GetResult());

            this.poaHeaderValidator.Sign(key, this.currentHeader.Header as PoABlockHeader);

            await this.signatureRule.RunAsync(ruleContext);
        }
    }
}
