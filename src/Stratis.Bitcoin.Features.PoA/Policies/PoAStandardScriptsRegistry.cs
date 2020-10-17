using System.Collections.Generic;
using NBitcoin;

namespace Stratis.Bitcoin.Features.PoA.Policies
{
    /// <summary>
    /// PoA-specific standard transaction definitions.
    /// </summary>
    public class PoAStandardScriptsRegistry : StandardScriptsRegistry
    {
        public const int MaxOpReturnRelay = 153;

        private static readonly List<ScriptTemplate> scriptTemplates = new List<ScriptTemplate>
        {
            { new PayToPubkeyHashTemplate() },
            { new PayToPubkeyTemplate() },
            { new PayToScriptHashTemplate() },
            { new PayToMultiSigTemplate() },
            { new PayToFederationTemplate() },
            { new TxNullDataTemplate(MaxOpReturnRelay) },
            { new PayToWitTemplate() }
        };

        public override List<ScriptTemplate> GetScriptTemplates => scriptTemplates;
    }
}