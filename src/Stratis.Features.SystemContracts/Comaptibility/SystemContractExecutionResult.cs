using System.Collections.Generic;
using NBitcoin;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.Receipts;

namespace Stratis.Features.SystemContracts.Comaptibility
{
    public class SystemContractExecutionResult : IContractExecutionResult
    {
        public SystemContractExecutionResult(uint160 to)
        {
            this.To = to;
        }

        public uint160 To { get; set; }

        public ContractErrorMessage ErrorMessage { get; set; }

        public ulong GasConsumed
        {
            get { return 0; }
            set { }
        }

        public uint160 NewContractAddress
        {
            get { return uint160.Zero; }
            set { }
        }

        public object Return
        {
            get { return null; }
            set { }
        }

        public bool Revert => false;

        public Transaction InternalTransaction
        {
            get { return null; }
            set { }
        }

        public ulong Fee
        {
            get { return 0; }
            set { }
        }

        public TxOut Refund
        {
            get { return null; }
            set { }
        }

        public IList<Log> Logs
        {
            get { return new List<Log>(); }
            set { }
        }
    }
}
