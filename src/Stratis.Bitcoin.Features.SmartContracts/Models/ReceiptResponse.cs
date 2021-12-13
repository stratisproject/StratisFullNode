using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.Receipts;

namespace Stratis.Bitcoin.Features.SmartContracts.Models
{
    public class ReceiptResponse
    {
        public string TransactionHash { get; }
        public string BlockHash { get; }
        public ulong? BlockNumber { get; }
        public string PostState { get; }
        public ulong GasUsed { get; }
        public string From { get; }
        public string To { get; }
        public string NewContractAddress { get; }
        public bool Success { get; }
        public string ReturnValue { get; }
        public string Bloom { get; }
        public string Error { get; }
        public LogResponse[] Logs { get; }
        public ReceiptResponse(Receipt receipt, List<LogResponse> logs, Network network)
        {
            this.TransactionHash = receipt.TransactionHash.ToString();
            this.BlockHash = receipt.BlockHash.ToString();
            this.BlockNumber = receipt.BlockNumber;
            this.PostState = receipt.PostState.ToString();
            this.GasUsed = receipt.GasUsed;
            this.From = receipt.From.ToBase58Address(network);
            this.To = receipt.To?.ToBase58Address(network);
            this.NewContractAddress = receipt.NewContractAddress?.ToBase58Address(network);
            this.ReturnValue = receipt.Result;
            this.Success = receipt.Success;
            this.Bloom = receipt.Bloom.ToString();
            this.Error = receipt.ErrorMessage;
            this.Logs = logs.ToArray();
        }
    }

    public class LogResponse
    {
        public string Address { get; }
        public string[] Topics { get; }
        public string Data { get; }

        public LogData Log { get; set; }

        public LogResponse(Log log, Network network)
        {
            this.Address = log.Address.ToBase58Address(network);
            this.Topics = log.Topics.Select(x => x.ToHexString()).ToArray();
            this.Data = log.Data.ToHexString();
        }
    }

    public class LogData
    {
        public LogData(string eventName, IDictionary<string, object> data)
        {
            this.@Event = eventName;

            this.Data = data;
        }

        public string Event { get; }

        public IDictionary<string, object> Data { get; }
    }

    public class LocalExecutionResponse
    {
        public IReadOnlyList<TransferResponse> InternalTransfers { get; set; }

        public Stratis.SmartContracts.RuntimeObserver.Gas GasConsumed { get; set; }

        public bool Revert { get; set; }

        public ContractErrorMessage ErrorMessage { get; set; }

        public object Return { get; set; }

        public IReadOnlyList<LogResponse> Logs { get; set; }
    }

    public class TransferResponse
    {
        public string From { get; set; }

        public string To { get; set; }

        public ulong Value { get; set; }
    }
}
