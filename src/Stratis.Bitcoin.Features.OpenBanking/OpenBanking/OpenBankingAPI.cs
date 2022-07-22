using System;
using System.Collections.Generic;
using NBitcoin;

namespace Stratis.Bitcoin.Features.OpenBanking.OpenBanking
{
    public class OBAmount
    {
        public string Amount { get; set; }
        public string Currency { get; set; }
    }

    public class OBTransaction
    {
        public string AccountId { get; set; }
        public string CreditDebitIndicator { get; set; }
        public string Status { get; set; }
        public string BookingDateTime { get; set; }
        public string ValueDateTime { get; set; }
        public string TransactionId { get; set; }
        public string TransactionReference { get; set; }
        public OBAmount Amount { get; set; }
    }

    public class OBTransactionData
    {
        public OBTransaction[] Transaction { get; set; }
    }

    public class OBGetTransactionsResponse
    {
        public OBTransactionData Data { get; set; }

        public IEnumerable<OpenBankDeposit> GetDeposits(string currency, Network network)
        {            
            foreach (OBTransaction obj in this.Data.Transaction)
            {
                if (obj.Amount.Currency != currency)
                    continue;

                if (obj.CreditDebitIndicator != "Credit")
                    continue;

                OpenBankDepositState state = OpenBankDepositState.Unknown;
                switch (obj.Status)
                {
                    case "Booked":
                        state = OpenBankDepositState.Booked;
                        break;
                    case "Pending":
                        state = OpenBankDepositState.Pending;
                        break;
                }

                var deposit = new OpenBankDeposit()
                {
                    BookDateTimeUTC = DateTime.Parse(obj.BookingDateTime),
                    ValueDateTimeUTC = DateTime.Parse(obj.ValueDateTime),
                    TransactionId = obj.TransactionId,
                    State = state,
                    Amount = Money.Parse(obj.Amount.Amount),
                    Reference = obj.TransactionReference
                };

                if (deposit.State == OpenBankDepositState.Booked && deposit.ParseAddressFromReference(network) == null)
                    deposit.State = OpenBankDepositState.Error;

                yield return deposit;
            }
        }
    }
}
