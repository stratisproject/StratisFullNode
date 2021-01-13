using System.Text;
using NBitcoin;
using Stratis.Features.FederatedPeg.Interfaces;

namespace Stratis.Features.FederatedPeg.Models
{
    public sealed class WithdrawalModel
    {
        public WithdrawalModel() { }

        public WithdrawalModel(Network network, ICrossChainTransfer transfer)
        {
            this.DepositId = transfer.DepositTransactionId;
            this.Id = transfer.PartialTransaction?.GetHash();
            this.Amount = transfer.DepositAmount;
            var target = transfer.DepositTargetAddress.GetDestinationAddress(network).ToString();
            this.PayingTo = target == network.CirrusRewardDummyAddress ? "Reward Distribution" : target;
            this.BlockHeight = transfer.BlockHeight ?? 0;
            this.BlockHash = transfer.BlockHash;
        }

        public WithdrawalModel(Network network, IWithdrawal withdrawal, ICrossChainTransfer transfer)
        {
            this.Id = withdrawal.Id;
            this.DepositId = withdrawal.DepositId;
            this.Amount = withdrawal.Amount;
            this.BlockHash = withdrawal.BlockHash;
            this.BlockHeight = withdrawal.BlockNumber;
            this.PayingTo = withdrawal.TargetAddress == network.CirrusRewardDummyAddress ? "Reward Distribution" : withdrawal.TargetAddress;
            this.TransferStatus = transfer?.Status.ToString();
        }

        public uint256 Id { get; set; }

        public uint256 DepositId { get; set; }

        public Money Amount { get; set; }

        public string PayingTo { get; set; }

        public int BlockHeight { get; set; }

        public uint256 BlockHash { get; set; }

        public string SpendingOutputDetails { get; set; }

        public string TransferStatus { get; set; }

        public override string ToString()
        {
            var stringBuilder = new StringBuilder();

            stringBuilder.Append(string.Format("Block Height={0,8} Paying={1} Amount={2,14} Status={3}",
                this.BlockHeight == 0 ? "Unconfirmed" : this.BlockHeight.ToString(),
                this.PayingTo,
                this.Amount.ToString(),
                this.TransferStatus));

            if (this.SpendingOutputDetails != null)
                stringBuilder.Append($" Spending={this.SpendingOutputDetails} ");

            return stringBuilder.ToString();
        }
    }
}
