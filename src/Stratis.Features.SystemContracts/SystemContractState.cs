using System;
using System.Collections.Generic;
using NBitcoin;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.ContractLogging;
using Stratis.SmartContracts.CLR.Serialization;
using Stratis.SmartContracts.Core.Receipts;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.Core.State.AccountAbstractionLayer;
using Stratis.SmartContracts.RuntimeObserver;

namespace Stratis.Features.SystemContracts
{
    public class SystemContractState : IState
    {
        private readonly List<TransferInfo> internalTransfers;
        private IState child;

        private SystemContractState(SystemContractState state)
        {
            this.ContractState = state.ContractState.StartTracking();

            // We create a new list but use references to the original transfers.
            this.internalTransfers = new List<TransferInfo>(state.InternalTransfers);

            // Create a new balance state based off the old one but with the repository and internal transfers list reference
            this.BalanceState = new BalanceState(this.ContractState, this.internalTransfers, state.BalanceState.InitialTransfer);
            this.Block = state.Block;
            this.TransactionHash = state.TransactionHash;
        }

        public SystemContractState()
        {
        }

        public IBlock Block { get; }

        public uint256 TransactionHash { get; }

        public BalanceState BalanceState { get; }

        public IStateRepository ContractState { get; private set; }

        public IReadOnlyList<TransferInfo> InternalTransfers { get; }

        public IContractLogHolder LogHolder => null;

        public NonceGenerator NonceGenerator => null;

        public void AddInitialTransfer(TransferInfo initialTransfer)
        {
            return;
        }

        public void AddInternalTransfer(TransferInfo transferInfo)
        {
            return;
        }

        public ISmartContractState CreateSmartContractState(IState state, IGasMeter gasMeter, uint160 address, BaseMessage message, IStateRepository repository)
        {
            throw new NotImplementedException();
        }

        public uint160 GenerateAddress(IAddressGenerator addressGenerator)
        {
            throw new NotImplementedException();
        }

        public ulong GetBalance(uint160 address)
        {
            return 0;
        }

        public IList<Log> GetLogs(IContractPrimitiveSerializer serializer)
        {
            return new List<Log>();
        }

        public IState Snapshot()
        {
            this.child = new SystemContractState(this);

            return this.child;
        }

        public void TransitionTo(IState state)
        {
            if (this.child != state)
            {
                throw new ArgumentException("New state must be a child of this state.");
            }

            // Update internal transfers
            this.internalTransfers.Clear();
            this.internalTransfers.AddRange(state.InternalTransfers);

            // Update logs
            this.LogHolder.Clear();
            this.LogHolder.AddRawLogs(state.LogHolder.GetRawLogs());

            // Commit the state to update the parent state
            state.ContractState.Commit();

            this.child = null;
        }
    }
}
