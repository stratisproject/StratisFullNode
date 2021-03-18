using System;
using System.Linq;
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
        private readonly Network network;
        private readonly IContractCodeHashingStrategy hashingStrategy;

        public PoSAllowedCodeHashLogic(Network network, IContractCodeHashingStrategy hashingStrategy)
        {
            this.network = network;
            this.hashingStrategy = hashingStrategy;
        }

        public void CheckContractTransaction(ContractTxData txData, Money suppliedBudget, int blockHeight = 0)
        {
            if (!txData.IsCreateContract)
                return;

            byte[] hashedCode = this.hashingStrategy.Hash(txData.ContractExecutionCode);
            
            if (this.network.Consensus.ConsensusFactory is SmartContractPoSConsensusFactory factory)
            {
                // Check the hashed code against all required signatures.
                try
                {
                    PubKey[] pubKeysPresented = txData.Signatures.Select(s => PubKey.RecoverFromMessage(hashedCode, s)).ToArray();
                    PubKey[] pubKeysRequired = factory.GetSignatureRequirements(blockHeight).ToArray();
                    if (pubKeysRequired.Any(r => !pubKeysPresented.Any(p => p == r)))
                        ThrowInvalidCode();

                    return;
                }
                catch (Exception)
                {
                    ThrowInvalidCode();
                }
            }

            ThrowInvalidCode();
        }

        public static void ThrowInvalidCode()
        {
            new ConsensusError("contract-code-invalid-hash", "Contract code does not have a valid hash").Throw();
        }
    }
}