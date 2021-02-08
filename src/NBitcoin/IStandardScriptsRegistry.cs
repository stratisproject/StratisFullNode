using System;
using System.Collections.Generic;
using NBitcoin.BitcoinCore;

namespace NBitcoin
{
    public interface IStandardScriptsRegistry
    {
        void RegisterStandardScriptTemplate(ScriptTemplate scriptTemplate);

        bool IsStandardTransaction(Transaction tx, Network network, int blockHeight = -1, uint256 blockHash = null);

        bool AreOutputsStandard(Network network, Transaction tx);

        ScriptTemplate GetTemplateFromScriptPubKey(Script script);

        bool IsStandardScriptPubKey(Network network, Script scriptPubKey);

        bool IsStandardScriptSig(Network network, Script scriptSig, Script scriptPubKey = null);

        bool AreInputsStandard(Network network, Transaction tx, CoinsView coinsView);

        List<ScriptTemplate> GetScriptTemplates { get; }

        ScriptTemplate this[Type key] { get; }
    }
}
