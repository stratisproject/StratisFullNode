using Stratis.Bitcoin.Tests.Common.TestFramework;
using Xunit;

// ReSharper disable ArrangeThisQualifier

namespace Stratis.Bitcoin.IntegrationTests.BlockStore
{
    public partial class ProofOfWorkSpendingSpecification : BddSpecification
    {
        /// <summary>
        /// Attempt_to_spend_coin_earned_through_proof_of_work_BEFORE_coin_maturity_will_fail
        /// </summary>
        [Fact]
        public void SpendBeforeMaturity()
        {
            Given(a_sending_and_receiving_stratis_bitcoin_node_and_wallet);
            And(a_block_is_mined_creating_spendable_coins);
            And(more_blocks_mined_to_just_BEFORE_maturity_of_original_block);
            When(spending_the_coins_from_original_block);
            Then(the_transaction_is_rejected_from_the_mempool);
        }

        /// <summary>
        /// Attempt_to_spend_coin_earned_through_proof_of_work_AFTER_maturity_will_succeed
        /// </summary>
        [Fact]
        public void SpendAfterMaturity()
        {
            Given(a_sending_and_receiving_stratis_bitcoin_node_and_wallet);
            And(a_block_is_mined_creating_spendable_coins);
            And(more_blocks_mined_to_just_AFTER_maturity_of_original_block);
            When(spending_the_coins_from_original_block);
            Then(the_transaction_is_put_in_the_mempool);
        }
    }
}
