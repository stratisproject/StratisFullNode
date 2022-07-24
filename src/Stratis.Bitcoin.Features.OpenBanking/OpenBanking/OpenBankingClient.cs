using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Text.Json;

namespace Stratis.Bitcoin.Features.OpenBanking.OpenBanking
{
    public interface IOpenBankingClient
    {
        OBGetTransactionsResponse GetTransactions(IOpenBankAccount openBankAccount, DateTime? fromBookingDateTime);
    }

    public class OpenBankingClient : IOpenBankingClient
    {
        private readonly string openBankingAPI = "https://api.alphabank.com/open-banking/v3.1/aisp";

        public OpenBankingClient()
        {
        }

        public OBGetTransactionsResponse GetTransactions(IOpenBankAccount openBankAccount, DateTime? fromBookingDateTime)
        {
            return new OBGetTransactionsResponse() { Data = new OBTransactionData() { Transaction = new OBTransaction[] { } } };

            // Transaction can be "Booked" or "Pending".
            // Need to revisit "Pending" in case their booking date changes? Up/Down???
            // https://openbanking.atlassian.net/wiki/spaces/DZ/pages/1004208451/Transactions+v3.1.1#Transactionsv3.1.1-GET%2Faccounts%2F%7BAccountId%7D%2Ftransactions

            string dateFilter = (fromBookingDateTime == null) ? "" : $"fromBookingDateTime={fromBookingDateTime.Value.ToString(":yyyy-MM-ddTHH:mm:ss")}";
            string url = $"{this.openBankingAPI}/accounts/{openBankAccount.OpenBankAccountNumber}/transactions?{dateFilter}";

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback = new RemoteCertificateValidationCallback(ServerCertificateValidation);

            Uri uri = new Uri(url);
            WebRequest webRequest = WebRequest.Create(uri);

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
            */

            WebResponse webResponse = webRequest.GetResponse();

            using (var r = webResponse.GetResponseStream())
            {
                using (var sr = new StreamReader(r))
                {
                    return JsonSerializer.Deserialize<OBGetTransactionsResponse>(sr.ReadToEnd());
                }
            }
        }

        private bool ServerCertificateValidation(object sender, System.Security.Cryptography.X509Certificates.X509Certificate certificate, System.Security.Cryptography.X509Certificates.X509Chain chain, System.Net.Security.SslPolicyErrors errors)
        {
            // Accepts all...
            return true;

            // TODO
            //return (errors == SslPolicyErrors.None) || certificate.GetCertHashString(HashAlgorithmName.SHA256).Equals("EB8E0B28AE064ED58CBED9DAEB46CFEB3BD7ECA677...");
        }
    }
}
