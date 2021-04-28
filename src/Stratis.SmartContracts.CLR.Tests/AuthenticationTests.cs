using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Patricia;
using Stratis.SmartContracts.CLR.Caching;
using Stratis.SmartContracts.CLR.Compilation;
using Stratis.SmartContracts.CLR.Loader;
using Stratis.SmartContracts.CLR.ResultProcessors;
using Stratis.SmartContracts.CLR.Serialization;
using Stratis.SmartContracts.CLR.Validation;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.Networks;
using Xunit;


namespace Stratis.SmartContracts.CLR.Tests
{
    public class AuthenticationTests
    {
        private const ulong BlockHeight = 0;
        private static readonly uint160 CoinbaseAddress = 0;
        private static readonly uint160 ToAddress = 1;
        private static readonly uint160 SenderAddress = 2;
        private static readonly Money MempoolFee = new Money(1_000_000);
        private readonly IKeyEncodingStrategy keyEncodingStrategy;
        private readonly ILoggerFactory loggerFactory;
        private readonly Network network;
        private readonly IContractRefundProcessor refundProcessor;
        private readonly IStateRepositoryRoot state;
        private readonly IContractTransferProcessor transferProcessor;
        private readonly SmartContractValidator validator;
        private IInternalExecutorFactory internalTxExecutorFactory;
        private readonly IContractAssemblyCache contractCache;
        private IVirtualMachine vm;
        private readonly ICallDataSerializer callDataSerializer;
        private readonly StateFactory stateFactory;
        private readonly IAddressGenerator addressGenerator;
        private readonly ILoader assemblyLoader;
        private readonly IContractModuleDefinitionReader moduleDefinitionReader;
        private readonly IContractPrimitiveSerializer contractPrimitiveSerializer;
        private readonly IStateProcessor stateProcessor;
        private readonly ISmartContractStateFactory smartContractStateFactory;
        private readonly ISerializer serializer;

        public AuthenticationTests()
        {
            this.keyEncodingStrategy = BasicKeyEncodingStrategy.Default;
            this.loggerFactory = ExtendedLoggerFactory.Create();
            this.network = new SmartContractsRegTest();
            this.refundProcessor = new ContractRefundProcessor(this.loggerFactory);
            this.state = new StateRepositoryRoot(new NoDeleteSource<byte[], byte[]>(new MemoryDictionarySource()));
            this.transferProcessor = new ContractTransferProcessor(this.loggerFactory, this.network);
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

            this.callDataSerializer = new CallDataSerializer(this.contractPrimitiveSerializer);

            this.stateFactory = new StateFactory(this.smartContractStateFactory);
        }

        [Fact]
        public void CanAuthenticate()
        {
            var internalTxExecutor = new Mock<IInternalTransactionExecutor>();
            var internalHashHelper = new Mock<IInternalHashHelper>();
            var persistentState = new TestPersistentState();
            var block = new Mock<IBlock>();
            var message = new Mock<IMessage>();
            Func<ulong> getBalance = () => 1;

            var contractPrimitiveSerializer = new ContractPrimitiveSerializer(this.network);
            var serializer = new Serializer(contractPrimitiveSerializer);

            ISmartContractState state = Mock.Of<ISmartContractState>(
                g => g.InternalTransactionExecutor == internalTxExecutor.Object
                     && g.InternalHashHelper == internalHashHelper.Object
                     && g.PersistentState == persistentState
                     && g.Block == block.Object
                     && g.Message == message.Object
                     && g.GetBalance == getBalance
                     && g.Serializer == serializer);

            IContract contract = Contract.CreateUninitialized(typeof(Authentication), state, new uint160(2));
            var instance = (Authentication)contract.GetPrivateFieldValue("instance");

            var keys = new[] { new Key(), new Key(), new Key(), new Key() };
            var keyIds = keys.Select(k => k.PubKey.Hash).ToArray();
            var addresses = keyIds.Select(id => id.ToBytes().ToAddress()).ToArray();

            // Initialize state.
            persistentState.SetArray("Signatories:main", addresses.Take(3).ToArray());
            persistentState.SetUInt32("Quorum:main", 2);

            var callGetSignatories = new MethodCall("GetSignatories", new object[] { "main" });
            IContractInvocationResult resultGetSignatories = contract.Invoke(callGetSignatories);
            Assert.True(resultGetSignatories.IsSuccess);
            Assert.Equal(3, ((Address[])resultGetSignatories.Return).Length);

            byte[] noSignatures = contractPrimitiveSerializer.Serialize(new string[] { });

            // First call the method without the sigatures.
            string addSignatoryChallenge = $"AddSignatory(Nonce:0,Group:main,Address:{addresses[3]},NewSize:4,NewQuorum:3)";
            string expectedAddSignatoryError = $"Please provide 2 valid signatures for '{addSignatoryChallenge}' from 'main'.";
            var callAddSignatory = new MethodCall("AddSignatory", new object[] { noSignatures, "main", addresses[3], (uint)4, (uint)3 });
            IContractInvocationResult resultAddSignatory = contract.Invoke(callAddSignatory);
            Assert.False(resultAddSignatory.IsSuccess);
            Assert.Contains(expectedAddSignatoryError, resultAddSignatory.ErrorMessage);

            // Now add the requested signatures.
            byte[] signatures = contractPrimitiveSerializer.Serialize(keys.Select(k => k.SignMessage(addSignatoryChallenge)).ToArray());
            var callAddSignatory2 = new MethodCall("AddSignatory", new object[] { signatures, "main", addresses[3], (uint)4, (uint)3 });
            IContractInvocationResult resultAddSignatory2 = contract.Invoke(callAddSignatory2);
            Assert.True(resultAddSignatory2.IsSuccess);

            var callGetSignatories2 = new MethodCall("GetSignatories", new object[] { "main" });
            IContractInvocationResult resultGetSignatories2 = contract.Invoke(callGetSignatories2);
            Assert.True(resultGetSignatories2.IsSuccess);
            Assert.Equal(4, ((Address[])resultGetSignatories2.Return).Length);

            var callGetQuorum = new MethodCall("GetQuorum", new object[] { "main" });
            IContractInvocationResult resultGetQuorum = contract.Invoke(callGetQuorum);
            Assert.True(resultGetQuorum.IsSuccess);
            Assert.Equal((uint)3, (uint)resultGetQuorum.Return);

            // First call the method without the sigatures.
            string removeSignatoryChallenge = $"RemoveSignatory(Nonce:1,Group:main,Address:{addresses[2]},NewSize:3,NewQuorum:2)";
            string expectedRemoveSignatoryError = $"Please provide 3 valid signatures for '{removeSignatoryChallenge}' from 'main'.";
            var callRemoveSignatory = new MethodCall("RemoveSignatory", new object[] { noSignatures, "main", addresses[2], (uint)3, (uint)2 });
            IContractInvocationResult resultRemoveSignatory = contract.Invoke(callRemoveSignatory);
            Assert.False(resultRemoveSignatory.IsSuccess);
            Assert.Contains(expectedRemoveSignatoryError, resultRemoveSignatory.ErrorMessage);

            signatures = contractPrimitiveSerializer.Serialize(keys.Select(k => k.SignMessage(removeSignatoryChallenge)).ToArray());
            var callRemoveSignatory2 = new MethodCall("RemoveSignatory", new object[] { signatures, "main", addresses[2], (uint)3, (uint)2 });
            IContractInvocationResult resultRemoveSignatory2 = contract.Invoke(callRemoveSignatory2);
            Assert.True(resultRemoveSignatory2.IsSuccess);

            var callGetSignatories3 = new MethodCall("GetSignatories", new object[] { "main" });
            IContractInvocationResult resultGetSignatories3 = contract.Invoke(callGetSignatories3);
            Assert.True(resultGetSignatories3.IsSuccess);
            Assert.Equal(3, ((Address[])resultGetSignatories3.Return).Length);

            var callGetQuorum2 = new MethodCall("GetQuorum", new object[] { "main" });
            IContractInvocationResult resultGetQuorum2 = contract.Invoke(callGetQuorum2);
            Assert.True(resultGetQuorum2.IsSuccess);
            Assert.Equal((uint)2, (uint)resultGetQuorum2.Return);
        }


        [Fact]
        public void AuthenticationInitializesAsExpected()
        {
            var internalTxExecutor = new Mock<IInternalTransactionExecutor>();
            var internalHashHelper = new Mock<IInternalHashHelper>();
            var persistentState = new TestPersistentState();
            var block = new Mock<IBlock>();
            var message = new Mock<IMessage>();
            Func<ulong> getBalance = () => 1;

            var contractPrimitiveSerializer = new ContractPrimitiveSerializer(this.network);
            var serializer = new Serializer(contractPrimitiveSerializer);

            ISmartContractState state = Mock.Of<ISmartContractState>(
                g => g.InternalTransactionExecutor == internalTxExecutor.Object
                     && g.InternalHashHelper == internalHashHelper.Object
                     && g.PersistentState == persistentState
                     && g.Block == block.Object
                     && g.Message == message.Object
                     && g.GetBalance == getBalance
                     && g.Serializer == serializer);

            Network network = new SmartContractsPoSRegTest();

            Authentication authentication = new Authentication(state, network, 1);

            Assert.True(persistentState.GetBool("Initialized"));
            
            var signatories = persistentState.GetArray<Address>("Signatories:main");

            string[] actual = signatories.Select(s => new KeyId(s.ToBytes()).GetAddress(network).ToString()).ToArray();

            Assert.Equal(network.SystemContractContainer.PrimaryAuthenticators.Signatories, actual);
            Assert.Equal(network.SystemContractContainer.PrimaryAuthenticators.Quorum, persistentState.GetUInt32("Quorum:main"));
        }
    }
}