namespace Stratis.SmartContracts.CLR
{
    public class SystemContractStateProcessor : IStateProcessor
    {
        public StateTransitionResult Apply(IState state, ExternalCreateMessage message)
        {
            throw new System.NotImplementedException();
        }

        public StateTransitionResult Apply(IState state, InternalCreateMessage message)
        {
            throw new System.NotImplementedException();
        }

        public StateTransitionResult Apply(IState state, InternalCallMessage message)
        {
            throw new System.NotImplementedException();
        }

        public StateTransitionResult Apply(IState state, ExternalCallMessage message)
        {
            throw new System.NotImplementedException();
        }

        public StateTransitionResult Apply(IState state, ContractTransferMessage message)
        {
            throw new System.NotImplementedException();
        }
    }
}