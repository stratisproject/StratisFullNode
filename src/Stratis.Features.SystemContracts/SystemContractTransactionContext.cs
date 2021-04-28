﻿using NBitcoin;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    public class SystemContractTransactionContext
    {
        public SystemContractTransactionContext(
            IStateRepositoryRoot state,
            Block block,
            Transaction transaction,
            SystemContractCall callData)
        {
            this.State = state;
            this.Block = block;
            this.Transaction = transaction;
            this.CallData = callData;
        }

        public Block Block { get; }

        public Transaction Transaction { get; }

        public SystemContractCall CallData { get; }

        public IStateRepositoryRoot State { get; }
    }
}
