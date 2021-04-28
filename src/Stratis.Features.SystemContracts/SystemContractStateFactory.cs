using System.Collections.Generic;
using NBitcoin;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    public class SystemContractStateFactory : IStateFactory
    {
        public IState Create(IStateRepository stateRoot, SmartContracts.IBlock block, ulong txOutValue, uint256 transactionHash)
        {
            return new SystemContractState(stateRoot, new List<SmartContracts.Core.State.AccountAbstractionLayer.TransferInfo>, block, transactionHash);
        }
    }
}
