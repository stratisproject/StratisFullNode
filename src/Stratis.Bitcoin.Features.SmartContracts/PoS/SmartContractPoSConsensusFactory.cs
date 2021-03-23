using System.Collections.Generic;
using System.Linq;
using NBitcoin;

namespace Stratis.Bitcoin.Features.SmartContracts.PoS
{
    public class SystemContractsSignatureRequirement
    {
        public PubKey PubKey { get; }

        public int? FromBlockHeight { get; }

        public int? ToBlockHeight { get; }

        public SystemContractsSignatureRequirement(PubKey pubKey, int? fromBlockHeight = null, int? toBlockHeight = null)
        {
            this.PubKey = pubKey;
            this.FromBlockHeight = fromBlockHeight;
            this.ToBlockHeight = toBlockHeight;
        }
    }

    public class SmartContractPoSConsensusFactory : PosConsensusFactory
    {
        private List<SystemContractsSignatureRequirement> signatures;

        public SmartContractPoSConsensusFactory(List<SystemContractsSignatureRequirement> systemContractsSignatureRequirements)
        {
            this.signatures = systemContractsSignatureRequirements;
        }
        public IEnumerable<PubKey> GetSignatureRequirements(int blockHeight)
        {
            return this.signatures.Where(s => (s.FromBlockHeight == null || s.FromBlockHeight <= blockHeight) && (s.ToBlockHeight == null || s.ToBlockHeight >= blockHeight)).Select(signatures => signatures.PubKey);
        }
    }
}
