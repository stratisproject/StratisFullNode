using System;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Features.MemoryPool.Interfaces;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.MemoryPool.Rules
{
    /// <summary>
    /// Validates the transaction with the coin view.
    /// Checks if already in coin view, and missing and unavailable inputs.
    /// </summary>
    public class CheckCoinViewMempoolRule : MempoolRule
    {
        public CheckCoinViewMempoolRule(Network network,
            ITxMempool mempool,
            MempoolSettings mempoolSettings,
            ChainIndexer chainIndexer,
            ILoggerFactory loggerFactory) : base(network, mempool, mempoolSettings, chainIndexer, loggerFactory)
        {
        }

        public override void CheckTransaction(MempoolValidationContext context)
        {
            Guard.Assert(context.View != null);

            context.LockPoints = new LockPoints();

            // Do we already have it?
            if (context.View.HaveTransaction(context.TransactionHash))
            {
                this.logger.LogTrace("(-)[INVALID_ALREADY_KNOWN]");
                context.State.Invalid(MempoolErrors.AlreadyKnown).Throw();
            }

            // Do all inputs exist?
            // Note that this does not check for the presence of actual outputs (see the next check for that),
            // and only helps with filling in pfMissingInputs (to determine missing vs spent).
            foreach (TxIn txin in context.Transaction.Inputs)
            {
                if (!context.View.HaveCoins(txin.PrevOut))
                {
                    context.State.MissingInputs = true;
                    this.logger.LogTrace("(-)[FAIL_MISSING_INPUTS]");
                    int height = 0;
                    try
                    {
                        height = context.View.GetTipHash()?.Height ?? 0;
                    }
                    catch (Exception) { }
                    this.logger.LogDebug($"Transaction '{context.TransactionHash}' has a missing input at height { height }: '{(txin.PrevOut.Hash.ToString() + "-" + txin.PrevOut.N)}'. The full transaction is '{context.Transaction.ToHex()}'.");
                    context.State.Fail(MempoolErrors.MissingOrSpentInputs).Throw();
                }
            }
        }
    }
}
