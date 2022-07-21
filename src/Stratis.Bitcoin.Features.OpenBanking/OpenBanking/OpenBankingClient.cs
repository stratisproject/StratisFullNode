﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using SimpleIdServer.OpenBankingApi;

namespace Stratis.Bitcoin.Features.OpenBanking.OpenBanking
{
    public interface IOpenBankingClient
    {
        IEnumerable<OpenBankDeposit> GetDeposits(IOpenBankAccount openBankAccount, DateTime? fromBookingDateTime);
    }

    public class OpenBankingClient : IOpenBankingClient
    {
        private readonly string openBankingAPI = "https://api.alphabank.com/open-banking/v3.1/aisp";
        private readonly Network network;

        public OpenBankingClient(OpenBankingSettings openBankingSettings, Network network, IServiceCollection serviceCollection)
        {
            this.network = network;

            // TODO:
            /*
            string token = "";
            string accountId = "";
            string issuer = "";
            var x = new SimpleIdServer.OpenBankingApi.Accounts.Queries.GetAccountQuery(token, accountId, issuer);
            var y = new SimpleIdServer.OpenBankingApi.OpenBankingApiBuilder(serviceCollection);
            //var z = new SimpleIdServer.OpenBankingApi.Api.Token.TokenBuilders.OpenBankingApiAccessTokenBuilder()
           */
        }

        public IEnumerable<OpenBankDeposit> GetDeposits(IOpenBankAccount openBankAccount, DateTime? fromBookingDateTime)
        {
            // Transaction can be "Booked" or "Pending".
            // Need to revisit "Pending" in case their booking date changes? Up/Down???
            // https://openbanking.atlassian.net/wiki/spaces/DZ/pages/1004208451/Transactions+v3.1.1#Transactionsv3.1.1-GET%2Faccounts%2F%7BAccountId%7D%2Ftransactions

            string dateFilter = (fromBookingDateTime == null) ? "" : $"fromBookingDateTime={fromBookingDateTime.Value.ToString(":yyyy-MM-ddTHH:mm:ss")}";
            string url = $"{this.openBankingAPI}/accounts/{openBankAccount.OpenBankAccountNumber}/transactions?{dateFilter}";

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback = new RemoteCertificateValidationCallback(ServerCertificateValidation);

            Uri uri = new Uri(url);
            WebRequest webRequest = WebRequest.Create(uri);
            WebResponse webResponse = webRequest.GetResponse();
            var r = webResponse.GetResponseStream();
            var sr = new StreamReader(r);
            var jsonObject = JsonSerializer.Deserialize<dynamic>(sr.ReadToEnd());
            var transactions = jsonObject.Data.Transaction;

            return ParseTransactions(openBankAccount, transactions);
        }

        private bool ServerCertificateValidation(object sender, System.Security.Cryptography.X509Certificates.X509Certificate certificate, System.Security.Cryptography.X509Certificates.X509Chain chain, System.Net.Security.SslPolicyErrors errors)
        {
            // Accepts all...
            return true;

            // TODO
            //return (errors == SslPolicyErrors.None) || certificate.GetCertHashString(HashAlgorithmName.SHA256).Equals("EB8E0B28AE064ED58CBED9DAEB46CFEB3BD7ECA677...");
        }

        private IEnumerable<OpenBankDeposit> ParseTransactions(IOpenBankAccount openBankAccount, dynamic[] transactions)
        {
            foreach (dynamic obj in transactions)
            {
                if (obj.Amount.Currency != openBankAccount.Currency)
                    continue;

                if (obj.CreditDebitIndicator != "Credit")
                    continue;

                if (obj.TransactionId.Length > 16)
                    continue;

                string externalId = obj.TransactionId.PadLeft(16, '0');

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
                    ExternalId = obj.TransactionId,
                    State = state,
                    Amount = Money.Parse(obj.Amount.Amount),
                    Reference = obj.TransactionReference
                };

                if (deposit.State == OpenBankDepositState.Booked && deposit.ParseAddressFromReference(this.network) == null)
                    deposit.State = OpenBankDepositState.Error;

                yield return deposit;
            }
        }

        /*
         GET /accounts/22289/transactions HTTP/1.1
            Authorization: Bearer Az90SAOJklae
            x-fapi-financial-id: OB/2017/001
            x-fapi-customer-last-logged-time:  Sun, 10 Sep 2017 19:43:31 GMT
            x-fapi-customer-ip-address: 104.25.212.99
            x-fapi-interaction-id: 93bac548-d2de-4546-b106-880a5018460d
            Accept: application/json

         HTTP/1.1 200 OK
            x-fapi-interaction-id: 93bac548-d2de-4546-b106-880a5018460d
            Content-Type: application/json
 
            {
              "Data": {
                "Transaction": [
                  {
                    "AccountId": "22289",
                    "TransactionId": "123",
                    "TransactionReference": "Ref 1",
                    "Amount": {
                      "Amount": "10.00",
                      "Currency": "GBP"
                    },
                    "CreditDebitIndicator": "Credit",
                    "Status": "Booked",
                    "BookingDateTime": "2017-04-05T10:43:07+00:00",
                    "ValueDateTime": "2017-04-05T10:45:22+00:00",
                    "TransactionInformation": "Cash from Aubrey",
                    "BankTransactionCode": {
                      "Code": "ReceivedCreditTransfer",
                      "SubCode": "DomesticCreditTransfer"
                    },
                    "ProprietaryBankTransactionCode": {
                      "Code": "Transfer",
                      "Issuer": "AlphaBank"
                    },
                    "Balance": {
                      "Amount": {
                        "Amount": "230.00",
                        "Currency": "GBP"
                      },
                      "CreditDebitIndicator": "Credit",
                      "Type": "InterimBooked"
                    }
                  }
                ]
              },
              "Links": {
                "Self": "https://api.alphabank.com/open-banking/v3.1/aisp/accounts/22289/transactions/"
              },
              "Meta": {
                "TotalPages": 1,
                "FirstAvailableDateTime": "2017-05-03T00:00:00+00:00",
                "LastAvailableDateTime": "2017-12-03T00:00:00+00:00"
              }
            }
        */
    }
}
