using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Utilities;

namespace Stratis.SmartContracts.Core.Util
{
    public class SenderRetriever : ISenderRetriever
    {
        public const string InvalidOutputIndex = "Invalid index given for PrevOut.";
        public const string OutputAlreadySpent = "Output has already been spent.";
        public const string OutputsNotInCoinView = "Unspent outputs to smart contract transaction are not present in coinview.";
        public const string UnableToGetSender ="Unable to get the sender of the transaction from previous transactions and null coinview.";


        /// <inheritdoc />
        public GetSenderResult GetSender(Transaction tx, ICoinView coinView, IList<Transaction> blockTxs)
        {
            OutPoint prevOut = tx.Inputs[0].PrevOut;

            // Check the txes in this block first
            if (blockTxs != null && blockTxs.Count > 0)
            {
                foreach (Transaction btx in blockTxs)
                {
                    if (btx.GetHash() == prevOut.Hash)
                    {
                        if (prevOut.N >= btx.Outputs.Count)
                        {
                            return GetSenderResult.CreateFailure(InvalidOutputIndex);
                        }

                        Script script = btx.Outputs[prevOut.N].ScriptPubKey;
                        return this.GetAddressFromScript(script);
                    }
                }
            }

            // Check the utxoset for the p2pk of the unspent output for this transaction
            if (coinView != null)
            {
                FetchCoinsResponse fetchCoinResult = coinView.FetchCoins(new OutPoint[] { prevOut });

                // The result from the coinview should never be null, so we do not check for that condition here.
                // It will simply not contain the requested outputs in the dictionary if they did not exist in the coindb.
                if (fetchCoinResult.UnspentOutputs.All(o => o.Key != prevOut))
                {
                    return GetSenderResult.CreateFailure(OutputsNotInCoinView);
                }
                
                UnspentOutput unspentOutputs = fetchCoinResult.UnspentOutputs.First(o => o.Key == prevOut).Value;

                // Since we now fetch a specific UTXO from the coindb instead of an entire transaction, it is no longer meaningful to check
                // (for the coindb at least - block transactions are handled separately above) whether the prevOut index is within bounds.
                // So that check has been removed from here and we proceed directly to checking spent-ness.
                if (unspentOutputs.Coins == null)
                {
                    return GetSenderResult.CreateFailure(OutputAlreadySpent);
                }

                TxOut senderOutput = unspentOutputs.Coins.TxOut;

                return this.GetAddressFromScript(senderOutput.ScriptPubKey);
            }

            return GetSenderResult.CreateFailure(UnableToGetSender);
        }

        public GetSenderResult GetSender(Transaction tx, MempoolCoinView coinView)
        {
            TxOut output = coinView.GetOutputFor(tx.Inputs[0]);
            return this.GetAddressFromScript(output.ScriptPubKey);
        }

        /// <inheritdoc />
        public GetSenderResult GetAddressFromScript(Script script)
        {
            PubKey payToPubKey = PayToPubkeyTemplate.Instance.ExtractScriptPubKeyParameters(script);

            if (payToPubKey != null)
            {
                var address = new uint160(payToPubKey.Hash.ToBytes());
                return GetSenderResult.CreateSuccess(address);
            }

            if (PayToPubkeyHashTemplate.Instance.CheckScriptPubKey(script))
            {
                var address = new uint160(PayToPubkeyHashTemplate.Instance.ExtractScriptPubKeyParameters(script).ToBytes());
                return GetSenderResult.CreateSuccess(address);
            }
            return GetSenderResult.CreateFailure("Addresses can only be retrieved from Pay to Pub Key or Pay to Pub Key Hash");
        }
    }
}
