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
    public class SystemContractDictionaryTests
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

        public SystemContractDictionaryTests()
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
        public void CanCompileSystemContractDictionary()
        {
            ContractCompilationResult compilationResult = ContractCompiler.CompileFile("SmartContracts/SystemContractsDictionary.cs");
            Assert.True(compilationResult.Success);
            byte[] contractCode = compilationResult.Compilation;

            var contractTxData = new ContractTxData(0, (RuntimeObserver.Gas)1, (RuntimeObserver.Gas)500_000, contractCode);
            var tx = new Transaction();
            tx.AddOutput(0, new Script(this.callDataSerializer.Serialize(contractTxData)));

            IContractTransactionContext transactionContext = new ContractTransactionContext(BlockHeight, CoinbaseAddress, MempoolFee, new uint160(2), tx);

            var executor = new ContractExecutor(
                this.callDataSerializer,
                this.state,
                this.refundProcessor,
                this.transferProcessor,
                this.stateFactory,
                this.stateProcessor,
                this.contractPrimitiveSerializer);

            IContractExecutionResult result = executor.Execute(transactionContext);

            Assert.Null(result.ErrorMessage);
        }

        [Fact]
        public void CanWhitelistSystemContracts()
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

            IContract contract = Contract.CreateUninitialized(typeof(SystemContractsDictionary), state, new uint160(2));
            var instance = (SystemContractsDictionary)contract.GetPrivateFieldValue("instance");

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
            string expectedAddSignatoryError = $"Please provide 2 valid signatures for '{addSignatoryChallenge}'.";
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
            string expectedRemoveSignatoryError = $"Please provide 3 valid signatures for '{removeSignatoryChallenge}'.";
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

            UInt256 codeHash = 1;
            string name = "Name";
            Address address = new Address(0, 0, 0, 0, 1);

            string whiteListChallenge = $"WhiteList(Nonce:0,CodeHash:{codeHash},LastAddress:{address},Name:Name)";
            string expectedWhiteListError = $"Please provide 2 valid signatures for '{whiteListChallenge}'.";
            var callWhiteList = new MethodCall("WhiteList", new object[] { noSignatures, codeHash, address, name });
            IContractInvocationResult resultWhiteList = contract.Invoke(callWhiteList);
            Assert.False(resultWhiteList.IsSuccess);
            Assert.Contains(expectedWhiteListError, resultWhiteList.ErrorMessage);

            signatures = contractPrimitiveSerializer.Serialize(keys.Select(k => k.SignMessage(whiteListChallenge)).ToArray());
            var callWhiteList2 = new MethodCall("WhiteList", new object[] { signatures, codeHash, address, name });
            IContractInvocationResult resultWhiteList2 = contract.Invoke(callWhiteList2);
            Assert.True(resultWhiteList2.IsSuccess);

            WhiteListEntry whiteListEntry = persistentState.GetStruct<WhiteListEntry>(codeHash.ToString());

            Assert.Equal(name, whiteListEntry.Name);
            Assert.Equal(codeHash, whiteListEntry.CodeHash);
            Assert.Equal(address, whiteListEntry.LastAddress);

            Assert.Equal(codeHash, persistentState.GetUInt256($"ByName:{name}"));

            var callIsWhiteListed = new MethodCall("IsWhiteListed", new object[] { codeHash });
            IContractInvocationResult resultIsWhiteListed = contract.Invoke(callIsWhiteListed);
            Assert.True((bool)resultIsWhiteListed.Return);

            var callGetCodeHash = new MethodCall("GetCodeHash", new object[] { name });
            IContractInvocationResult resultGetCodeHash = contract.Invoke(callGetCodeHash);
            Assert.Equal(codeHash, (UInt256)resultGetCodeHash.Return);

            var callGetContractAddress = new MethodCall("GetContractAddress", new object[] { name });
            IContractInvocationResult resultGetContractAddress = contract.Invoke(callGetContractAddress);
            Assert.Equal(address, (Address)resultGetContractAddress.Return);

            var callGetContractAddressCH = new MethodCall("GetContractAddress", new object[] { codeHash });
            IContractInvocationResult resultGetContractAddressCH = contract.Invoke(callGetContractAddressCH);
            Assert.Equal(address, (Address)resultGetContractAddressCH.Return);

            string blackListChallenge = $"BlackList(Nonce:1,CodeHash:{codeHash},LastAddress:{address},Name:Name)";
            string expectedBlackListError = $"Please provide 2 valid signatures for '{blackListChallenge}'.";
            var callBlackList = new MethodCall("BlackList", new object[] { noSignatures, codeHash });
            IContractInvocationResult resultBlackList = contract.Invoke(callBlackList);
            Assert.False(resultBlackList.IsSuccess);
            Assert.Contains(expectedBlackListError, resultBlackList.ErrorMessage);

            signatures = contractPrimitiveSerializer.Serialize(keys.Select(k => k.SignMessage(blackListChallenge)).ToArray());
            var callBlackList2 = new MethodCall("BlackList", new object[] { signatures, codeHash });
            IContractInvocationResult resultBlackList2 = contract.Invoke(callBlackList2);
            Assert.True(resultBlackList2.IsSuccess);

            var callIsWhiteListed2 = new MethodCall("IsWhiteListed", new object[] { codeHash });
            IContractInvocationResult resultIsWhiteListed2 = contract.Invoke(callIsWhiteListed2);
            Assert.False((bool)resultIsWhiteListed2.Return);

            // These methods don't return anything once the conteact is black-listed.

            Assert.Equal(default(UInt256), persistentState.GetUInt256($"ByName:{name}"));

            var callGetCodeHash2 = new MethodCall("GetCodeHash", new object[] { name });
            IContractInvocationResult resultGetCodeHash2 = contract.Invoke(callGetCodeHash2);
            Assert.Equal(default(UInt256), (UInt256)resultGetCodeHash2.Return);

            var callGetContractAddress2 = new MethodCall("GetContractAddress", new object[] { name });
            IContractInvocationResult resultGetContractAddress2 = contract.Invoke(callGetContractAddress2);
            Assert.Equal(default(Address), (Address)resultGetContractAddress2.Return);

            var callGetContractAddressCH2 = new MethodCall("GetContractAddress", new object[] { codeHash });
            IContractInvocationResult resultGetContractAddressCH2 = contract.Invoke(callGetContractAddressCH2);
            Assert.Equal(default(Address), (Address)resultGetContractAddressCH2.Return);
        }
    }
}