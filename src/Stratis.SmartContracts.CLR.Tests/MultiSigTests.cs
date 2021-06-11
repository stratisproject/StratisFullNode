using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.SmartContracts.CLR.Caching;
using Stratis.SmartContracts.CLR.Compilation;
using Stratis.SmartContracts.CLR.Loader;
using Stratis.SmartContracts.CLR.Serialization;
using Stratis.SmartContracts.CLR.Validation;
using Stratis.SmartContracts.Networks;
using Xunit;


namespace Stratis.SmartContracts.CLR.Tests
{
    public class MultiSigTests
    {
        private readonly ILoggerFactory loggerFactory;
        private readonly Network network;
        private readonly SmartContractValidator validator;
        private IInternalExecutorFactory internalTxExecutorFactory;
        private readonly IContractAssemblyCache contractCache;
        private IVirtualMachine vm;
        private readonly IAddressGenerator addressGenerator;
        private readonly ILoader assemblyLoader;
        private readonly IContractModuleDefinitionReader moduleDefinitionReader;
        private readonly IContractPrimitiveSerializer contractPrimitiveSerializer;
        private readonly IStateProcessor stateProcessor;
        private readonly ISmartContractStateFactory smartContractStateFactory;
        private readonly ISerializer serializer;

        public MultiSigTests()
        {            
            this.loggerFactory = ExtendedLoggerFactory.Create();
            this.network = new SmartContractsPoSRegTest();
            this.validator = new SmartContractValidator();
            this.addressGenerator = new AddressGenerator();
            this.assemblyLoader = new ContractAssemblyLoader();
            this.moduleDefinitionReader = new ContractModuleDefinitionReader();
            this.contractPrimitiveSerializer = new ContractPrimitiveSerializer(this.network);
            this.serializer = new Serializer(this.contractPrimitiveSerializer);
            this.contractCache = new ContractAssemblyCache();
            this.vm = new ReflectionVirtualMachine(this.validator, this.loggerFactory, this.assemblyLoader, this.moduleDefinitionReader, this.contractCache);
            this.stateProcessor = new StateProcessor(this.vm, this.addressGenerator);
            this.internalTxExecutorFactory = new InternalExecutorFactory(this.loggerFactory, this.stateProcessor);
            this.smartContractStateFactory = new SmartContractStateFactory(this.contractPrimitiveSerializer, this.internalTxExecutorFactory, this.serializer);
        }

        [Fact]
        public void CanAddAndRemoveMembers()
        {
            var context = new ContractExecutorTestContext(new SmartContractsPoSRegTest());
            var internalTxExecutor = new Mock<IInternalTransactionExecutor>();
            var internalHashHelper = new Mock<IInternalHashHelper>();
            var testAddress = ((uint160)new EmbeddedContractAddress(typeof(MultiSig), 1)).ToAddress();
            var persistentState = new PersistentState(
                context.PersistenceStrategy,
                context.Serializer, testAddress.ToUint160());
            IBlock block = new Block(1, testAddress);
            IMessage message = new Message(testAddress, testAddress, 0);
            Func<ulong> getBalance = () => 1;

            ISmartContractState state = Mock.Of<ISmartContractState>(
                g => g.InternalTransactionExecutor == internalTxExecutor.Object
                     && g.InternalHashHelper == internalHashHelper.Object
                     && g.PersistentState == persistentState
                     && g.Block == block
                     && g.Message == message
                     && g.GetBalance == getBalance
                     && g.Serializer == context.Serializer);

            IContract contract = Contract.CreateUninitialized(typeof(MultiSig), state, new uint160(3));
            var instance = (MultiSig)contract.GetPrivateFieldValue("instance");

            var result = contract.InvokeConstructor(new object[] { context.PersistenceStrategy, this.network });

            Assert.True(persistentState.GetBool("Initialized"));

            var authPersistentState = new PersistentState(context.PersistenceStrategy, context.Serializer, new EmbeddedContractAddress(typeof(Authentication), 1));

            var federationDetails = this.network.Federations.GetOnlyFederation().GetFederationDetails();
            var federationId = this.network.Federations.GetFederations().Single().Id.ToHex(this.network);

            var signatories = persistentState.GetArray<string>($"Members:{federationId}");

            PubKey[] actual = signatories.Select(s => new PubKey(s)).ToArray();

            Assert.Equal(federationDetails.transactionSigningKeys, actual);
            Assert.Equal((uint)federationDetails.signaturesRequired, persistentState.GetUInt32($"Quorum:{federationId}"));

            var keys = new[] { new Key(), new Key(), new Key(), new Key() };
            var keyIds = keys.Select(k => k.PubKey.Hash).ToArray();
            var pubKeys = keys.Select(k => k.PubKey.ToHex(this.network)).ToArray();
            var addresses = keyIds.Select(id => id.ToBytes().ToAddress()).ToArray();

            // Initialize state.
            authPersistentState.SetArray("Signatories:main", addresses.Take(3).ToArray());
            authPersistentState.SetUInt32("Quorum:main", 2);

            var callGetSignatories = new MethodCall("GetFederationMembers", new object[] { federationId });
            IContractInvocationResult resultGetSignatories = contract.Invoke(callGetSignatories);
            Assert.True(resultGetSignatories.IsSuccess);
            Assert.Equal(3, ((string[])resultGetSignatories.Return).Length);

            byte[] noSignatures = contractPrimitiveSerializer.Serialize(new string[] { });

            // First call the method without the sigatures.
            string addSignatoryChallenge = $"AddMember(Nonce:0,FederationId:{federationId},PubKey:{pubKeys[3]},NewSize:4,NewQuorum:3)";
            string expectedAddSignatoryError = $"Please provide 2 valid signatures for '{addSignatoryChallenge}' from 'main'.";
            var callAddSignatory = new MethodCall("AddMember", new object[] { noSignatures, federationId, pubKeys[3], (uint)4, (uint)3 });
            IContractInvocationResult resultAddSignatory = contract.Invoke(callAddSignatory);
            Assert.False(resultAddSignatory.IsSuccess);
            Assert.Contains(expectedAddSignatoryError, resultAddSignatory.ErrorMessage);

            // Now add the requested signatures.
            byte[] signatures = contractPrimitiveSerializer.Serialize(keys.Select(k => k.SignMessage(addSignatoryChallenge)).ToArray());
            var callAddSignatory2 = new MethodCall("AddMember", new object[] { signatures, federationId, pubKeys[3], (uint)4, (uint)3 });
            IContractInvocationResult resultAddSignatory2 = contract.Invoke(callAddSignatory2);
            Assert.True(resultAddSignatory2.IsSuccess);

            var callGetSignatories2 = new MethodCall("GetFederationMembers", new object[] { federationId });
            IContractInvocationResult resultGetSignatories2 = contract.Invoke(callGetSignatories2);
            Assert.True(resultGetSignatories2.IsSuccess);
            Assert.Equal(4, ((string[])resultGetSignatories2.Return).Length);

            var callGetQuorum = new MethodCall("GetFederationQuorum", new object[] { federationId });
            IContractInvocationResult resultGetQuorum = contract.Invoke(callGetQuorum);
            Assert.True(resultGetQuorum.IsSuccess);
            Assert.Equal((uint)3, (uint)resultGetQuorum.Return);

            // First call the method without the signatures.
            string removeSignatoryChallenge = $"RemoveMember(Nonce:1,FederationId:{federationId},PubKey:{pubKeys[3]},NewSize:3,NewQuorum:2)";
            string expectedRemoveSignatoryError = $"Please provide 2 valid signatures for '{removeSignatoryChallenge}' from 'main'.";
            var callRemoveSignatory = new MethodCall("RemoveMember", new object[] { noSignatures, federationId, pubKeys[3], (uint)3, (uint)2 });
            IContractInvocationResult resultRemoveSignatory = contract.Invoke(callRemoveSignatory);
            Assert.False(resultRemoveSignatory.IsSuccess);
            Assert.Contains(expectedRemoveSignatoryError, resultRemoveSignatory.ErrorMessage);

            signatures = contractPrimitiveSerializer.Serialize(keys.Select(k => k.SignMessage(removeSignatoryChallenge)).ToArray());
            var callRemoveSignatory2 = new MethodCall("RemoveMember", new object[] { signatures, federationId, pubKeys[3], (uint)3, (uint)2 });
            IContractInvocationResult resultRemoveSignatory2 = contract.Invoke(callRemoveSignatory2);
            Assert.True(resultRemoveSignatory2.IsSuccess);

            var callGetSignatories3 = new MethodCall("GetFederationMembers", new object[] { federationId });
            IContractInvocationResult resultGetSignatories3 = contract.Invoke(callGetSignatories3);
            Assert.True(resultGetSignatories3.IsSuccess);
            Assert.Equal(3, ((string[])resultGetSignatories3.Return).Length);

            var callGetQuorum2 = new MethodCall("GetFederationQuorum", new object[] { federationId });
            IContractInvocationResult resultGetQuorum2 = contract.Invoke(callGetQuorum2);
            Assert.True(resultGetQuorum2.IsSuccess);
            Assert.Equal((uint)2, (uint)resultGetQuorum2.Return);
        }
    }
}