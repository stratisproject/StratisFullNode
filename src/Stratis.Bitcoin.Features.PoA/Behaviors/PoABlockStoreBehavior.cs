using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.PoA.Payloads;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.P2P.Protocol.Payloads;

namespace Stratis.Bitcoin.Features.PoA.Behaviors
{
    public sealed class PoABlockStoreBehavior : BlockStoreBehavior
    {
        public PoABlockStoreBehavior(ChainIndexer chainIndexer, IChainState chainState, ILoggerFactory loggerFactory, IConsensusManager consensusManager, IBlockStoreQueue blockStoreQueue)
            : base(chainIndexer, chainState, loggerFactory, consensusManager, blockStoreQueue)
        {
        }

        /// <inheritdoc />
        protected override Payload BuildHeadersAnnouncePayload(IEnumerable<ChainedHeader> headers)
        {
            var poaHeaders = headers.Select(s => s.Header).Cast<PoABlockHeader>().ToList();
            return new PoAHeadersPayload(poaHeaders);
        }

        public override object Clone()
        {
            var clone = new PoABlockStoreBehavior(this.ChainIndexer, this.chainState, this.loggerFactory, this.consensusManager, this.blockStoreQueue)
            {
                CanRespondToGetDataPayload = this.CanRespondToGetDataPayload
            };

            return clone;
        }
    }
}
