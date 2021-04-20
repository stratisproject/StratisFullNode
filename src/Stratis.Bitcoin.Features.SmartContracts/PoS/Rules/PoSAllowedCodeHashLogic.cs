using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.SmartContracts.Interfaces;
using Stratis.Bitcoin.Features.SmartContracts.Rules;
using Stratis.SmartContracts.CLR;

namespace Stratis.Bitcoin.Features.SmartContracts.PoS.Rules
{
    /// <summary>
    /// Validates that the hash of the supplied smart contract code is allowed.
    /// </summary>
    public class PoSAllowedCodeHashLogic : IContractTransactionFullValidationRule
    {
        private static uint256 dictionaryCodeHash = new uint256("");

        private readonly Network network;
        private readonly IContractCodeHashingStrategy hashingStrategy;

        public PoSAllowedCodeHashLogic(Network network, IContractCodeHashingStrategy hashingStrategy)
        {
            this.network = network;
            this.hashingStrategy = hashingStrategy;
        }
       
        /// <summary>
        /// Checks that "create" contract transactions meet the allowed code hash requirements.
        /// </summary>
        /// <param name="txData">Defines the contract being created.</param>
        /// <param name="suppliedBudget">The supplied budget.</param>
        /// <param name="blockHeight">The block height that helps determine the current signature requirements.</param>
        public void CheckContractTransaction(ContractTxData txData, Money suppliedBudget, int blockHeight = 0)
        {
            if (!txData.IsCreateContract)
                return;

            uint256 hashedCode = new uint256(this.hashingStrategy.Hash(txData.ContractExecutionCode));

            if (hashedCode == dictionaryCodeHash)
                return;

            // TODO: If the contract is white-listed in the dictionary contract then exit without throwing an error.

            ThrowInvalidCode();
        }

        private static void ThrowInvalidCode()
        {
            new ConsensusError("contract-code-invalid-hash", "Contract code does not have a valid hash").Throw();
        }
    }
}