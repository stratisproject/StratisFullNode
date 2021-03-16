using System.Linq;
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
        private readonly Network network;
        private readonly IContractCodeHashingStrategy hashingStrategy;

        public PoSAllowedCodeHashLogic(Network network, IContractCodeHashingStrategy hashingStrategy)
        {
            this.network = network;
            this.hashingStrategy = hashingStrategy;
        }

        public void CheckContractTransaction(ContractTxData txData, Money suppliedBudget)
        {
            if (!txData.IsCreateContract)
                return;

            byte[] hashedCode = this.hashingStrategy.Hash(txData.ContractExecutionCode);
            
            if (this.network.Consensus.ConsensusFactory is SmartContractPoSConsensusFactory factory)
            {
                PubKey[] pubKeys = factory.GetSignatureRequirements(0).ToArray();
                // TODO: Check the hashed code against all required signatures.
            }

            ThrowInvalidCode();
        }

        public static void ThrowInvalidCode()
        {
            new ConsensusError("contract-code-invalid-hash", "Contract code does not have a valid hash").Throw();
        }
    }
}