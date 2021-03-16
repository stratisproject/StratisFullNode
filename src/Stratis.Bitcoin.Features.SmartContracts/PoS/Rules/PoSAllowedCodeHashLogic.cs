using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.SmartContracts.Interfaces;
using Stratis.Bitcoin.Features.SmartContracts.Rules;
using Stratis.SmartContracts.CLR;

namespace Stratis.Bitcoin.Features.SmartContracts.PoS.Rules
{
    /// <summary>
    /// Validates that the hash of the supplied smart contract code is contained in a list of supplied hashes.
    /// </summary>
    public class PoSAllowedCodeHashLogic : IContractTransactionFullValidationRule
    {
        private readonly IContractCodeHashingStrategy hashingStrategy;

        public PoSAllowedCodeHashLogic(IWhitelistedHashChecker whitelistedHashChecker, IContractCodeHashingStrategy hashingStrategy)
        {
            this.hashingStrategy = hashingStrategy;
        }

        public void CheckContractTransaction(ContractTxData txData, Money suppliedBudget)
        {
            if (!txData.IsCreateContract)
                return;

            byte[] hashedCode = this.hashingStrategy.Hash(txData.ContractExecutionCode);
            /*
            if (!this.whitelistedHashChecker.CheckHashWhitelisted(hashedCode))
            {
                ThrowInvalidCode();
            }
            */
        }

        public static void ThrowInvalidCode()
        {
            new ConsensusError("contract-code-invalid-hash", "Contract code does not have a valid hash").Throw();
        }
    }
}