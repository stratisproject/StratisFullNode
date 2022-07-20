using NBitcoin;
using Stratis.Bitcoin.Features.OpenBanking.OpenBanking;

namespace Stratis.Bitcoin.Features.OpenBanking.TokenMinter
{
    public interface ITokenMintingTransactionBuilder
    {
        Transaction BuildSignedTransaction(IOpenBankAccount openBankAccount, OpenBankDeposit openBankDeposit);
    }
}
