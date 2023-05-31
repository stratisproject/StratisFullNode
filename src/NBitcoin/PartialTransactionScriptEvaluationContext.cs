using System.Linq;

namespace NBitcoin
{
    public class PartialTransactionScriptEvaluationContext : ScriptEvaluationContext
    {
        public PartialTransactionScriptEvaluationContext(Network network) : base(network)
        {
        }

        public override (bool success, bool isError) DetermineSignatures(ref int i, bool fRequireMinimal, ref int nKeysCount, int pbegincodehash, Script s, int hashversion, ref int ikey, TransactionChecker checker)
        {
            int nSigsCount = new CScriptNum(this._stack.Top(-i), fRequireMinimal).getint();
            if (nSigsCount < 0 || nSigsCount > nKeysCount)
                return (false, !SetError(ScriptError.SigCount));

            int isig = ++i;
            i += nSigsCount;
            if (this._stack.Count < i)
                return (false, !SetError(ScriptError.InvalidStackOperation));

            // Subset of script starting at the most recent codeseparator
            var scriptCode = new Script(s._Script.Skip(pbegincodehash).ToArray());
            // Drop the signatures, since there's no way for a signature to sign itself
            for (int k = 0; k < nSigsCount; k++)
            {
                byte[] vchSig = this._stack.Top(-isig - k);
                if (hashversion == (int)HashVersion.Original)
                    scriptCode.FindAndDelete(vchSig);
            }

            bool fSuccess = true;
            while (fSuccess && nSigsCount > 0)
            {
                byte[] vchSig = this._stack.Top(-isig);

                // If the signature at the particular index in the stack is empty,
                // move onto the next one.
                if(vchSig.Length == 0)
                {
                    isig++;
                    continue;
                }
                
                byte[] vchPubKey = this._stack.Top(-ikey);

                // Note how this makes the exact order of pubkey/signature evaluation
                // distinguishable by CHECKMULTISIG NOT if the STRICTENC flag is set.
                // See the script_(in)valid tests for details.
                if (!CheckSignatureEncoding(vchSig) || !CheckPubKeyEncoding(vchPubKey, hashversion))
                {
                    // serror is set
                    return (false, true);
                }

                bool fOk = CheckSig(vchSig, vchPubKey, scriptCode, checker, hashversion);

                if (fOk)
                {
                    isig++;
                    nSigsCount--;
                }
                ikey++;
                nKeysCount--;

                // If there are more signatures left than keys left,
                // then too many signatures have failed
                if (nSigsCount > nKeysCount)
                    fSuccess = false;
            }

            return (fSuccess, false);
        }
    }
}
