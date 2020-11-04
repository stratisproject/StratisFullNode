using System;
using System.Collections.Generic;
using System.Net.Http;
using Flurl;
using Flurl.Http;
using Newtonsoft.Json;

namespace SwapExtractionTool
{
    public class BlockExplorerClient : IDisposable
    {
        private readonly string baseUrl;

        private HttpClient httpClient;

        public BlockExplorerClient(string baseUrl)
        {
            this.baseUrl = baseUrl;
            this.httpClient = new HttpClient();
        }

        public TransactionModel GetTransaction(string txId)
        {
            TransactionModel result = $"{this.baseUrl}"
                .AppendPathSegment($"transactions/")
                .AppendPathSegment($"{txId}")
                .GetJsonAsync<TransactionModel>().GetAwaiter().GetResult();

            return result;
        }

        public void Dispose()
        {
            this.httpClient?.Dispose();
        }
    }

    // Auto-generated models

    public class Amount
    {
        public long satoshi { get; set; }
    }

    public class Fee
    {
        public int satoshi { get; set; }
    }

    public class Amount2
    {
        public long satoshi { get; set; }
    }

    public class In
    {
        public string hash { get; set; }
        public Amount2 amount { get; set; }
        public int n { get; set; }
    }

    public class Amount3
    {
        public long satoshi { get; set; }
    }

    public class Out
    {
        public string hash { get; set; }
        public Amount3 amount { get; set; }
        public int n { get; set; }
    }

    public class TransactionModel
    {
        public string hash { get; set; }
        public bool isCoinbase { get; set; }
        public bool isCoinstake { get; set; }
        public bool isSmartContract { get; set; }
        public Amount amount { get; set; }
        public Fee fee { get; set; }
        public int height { get; set; }
        public DateTime firstSeen { get; set; }
        public int time { get; set; }
        public bool spent { get; set; }

        [JsonProperty(PropertyName = "in")]
        public List<In> _in { get; set; }

        [JsonProperty(PropertyName = "out")]
        public List<Out> _out { get; set; }
        
        public int confirmations { get; set; }
    }
}
