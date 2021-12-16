﻿using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.MemoryPool.Interfaces;

namespace Stratis.Bitcoin.Features.SmartContracts.MempoolRules
{
    /// <summary>
    /// Checks that transactions sent to the mempool don't include the OP_SPEND opcode.
    /// </summary>
    public class OpSpendMempoolRule : MempoolRule
    {
        public OpSpendMempoolRule(Network network,
            ITxMempool mempool,
            MempoolSettings mempoolSettings,
            ChainIndexer chainIndexer,
            ILoggerFactory loggerFactory) : base(network, mempool, mempoolSettings, chainIndexer, loggerFactory)
        {
        }

        /// <inheritdoc/>
        public override void CheckTransaction(MempoolValidationContext context)
        {
            if (context.Transaction.Inputs.Any(x => x.ScriptSig.IsSmartContractSpend()) || context.Transaction.Outputs.Any(x => x.ScriptPubKey.IsSmartContractSpend()))
                this.Throw();
        }

        private void Throw()
        {
            new ConsensusError("opspend-in-mempool", "opspend shouldn't be in transactions created by users").Throw();
        }
    }
}