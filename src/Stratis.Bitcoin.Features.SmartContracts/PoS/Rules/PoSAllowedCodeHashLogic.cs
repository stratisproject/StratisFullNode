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

        /// <summary>
        /// Provides a message to sign to allow code via a code hash.
        /// </summary>
        /// <param name="hashedCode">The code hash of the code to allow.</param>
        /// <returns>Message to sign to allow code via a code hash</returns>
        public static string MessageToAllowCode(byte[] hashedCode)
        {
            return $"Allow code {(new uint256(hashedCode))}";
        }

        /// <summary>
        /// Verifies that a list of signatures provided with a message meets the current network requirements
        /// for attaining the post-deployment configuration privilege described by the message.
        /// </summary>
        /// <param name="message">The message to test the signatures against.</param>
        /// <param name="signatures">The signatures provided.</param>
        /// <param name="blockHeight">The block height that helps determine the current signature requirements.</param>
        public void VerifySignatures(string message, string[] signatures, int blockHeight)
        {
            if (this.network.Consensus.ConsensusFactory is SmartContractPoSConsensusFactory factory)
            {
                // Check the hashed code against all required signatures.
                try
                {
                    PubKey[] pubKeysPresented = signatures.Select(s => PubKey.RecoverFromMessage(message, s)).ToArray();
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

        /// <summary>
        /// Checks that "create" constract transactions meet the current signature requirements.
        /// </summary>
        /// <param name="txData">Defines the contract being created.</param>
        /// <param name="suppliedBudget">The supplied budget.</param>
        /// <param name="blockHeight">The block height that helps determine the current signature requirements.</param>
        public void CheckContractTransaction(ContractTxData txData, Money suppliedBudget, int blockHeight = 0)
        {
            if (!txData.IsCreateContract)
                return;

            byte[] hashedCode = this.hashingStrategy.Hash(txData.ContractExecutionCode);

            this.VerifySignatures(MessageToAllowCode(hashedCode), txData.Signatures, blockHeight);
        }

        private static void ThrowInvalidCode()
        {
            new ConsensusError("contract-code-invalid-hash", "Contract code does not have a valid hash").Throw();
        }
    }
}