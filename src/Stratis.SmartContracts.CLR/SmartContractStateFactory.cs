﻿using NBitcoin;
using Stratis.SmartContracts.CLR.ContractLogging;
using Stratis.SmartContracts.CLR.Serialization;
using Stratis.SmartContracts.Core.State;

namespace Stratis.SmartContracts.CLR
{
    public class SmartContractStateFactory : ISmartContractStateFactory
    {
        private readonly ISerializer serializer;

        private readonly IEcRecoverProvider ecRecoverProvider;

        public SmartContractStateFactory(IContractPrimitiveSerializer primitiveSerializer,
            IInternalExecutorFactory internalTransactionExecutorFactory,
            ISerializer serializer,
            IEcRecoverProvider ecRecoverProvider)
        {
            this.serializer = serializer;
            this.PrimitiveSerializer = primitiveSerializer;
            this.InternalTransactionExecutorFactory = internalTransactionExecutorFactory;
            this.ecRecoverProvider = ecRecoverProvider;
        }

        public IContractPrimitiveSerializer PrimitiveSerializer { get; }
        public IInternalExecutorFactory InternalTransactionExecutorFactory { get; }

        /// <summary>
        /// Sets up a new <see cref="ISmartContractState"/> based on the current state.
        /// </summary>        
        public ISmartContractState Create(IState state, RuntimeObserver.IGasMeter gasMeter, uint160 address, BaseMessage message, IStateRepository repository)
        {
            IPersistenceStrategy persistenceStrategy = new MeteredPersistenceStrategy(repository, gasMeter, new BasicKeyEncodingStrategy());

            var persistentState = new PersistentState(persistenceStrategy, this.serializer, address);

            var contractLogger = new MeteredContractLogger(gasMeter, state.LogHolder, this.PrimitiveSerializer);

            var contractState = new SmartContractState(
                state.Block,
                new Message(
                    address.ToAddress(),
                    message.From.ToAddress(),
                    message.Amount
                ),
                persistentState,
                this.serializer,
                contractLogger,
                this.InternalTransactionExecutorFactory.Create(gasMeter, state),
                new InternalHashHelper(),
                () => state.GetBalance(address),
                this.ecRecoverProvider);

            return contractState;
        }
    }
}