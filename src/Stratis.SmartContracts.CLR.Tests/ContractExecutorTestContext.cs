using System;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Patricia;
using Stratis.SmartContracts.CLR.Caching;
using Stratis.SmartContracts.CLR.Compilation;
using Stratis.SmartContracts.CLR.Loader;
using Stratis.SmartContracts.CLR.Serialization;
using Stratis.SmartContracts.CLR.Validation;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.Networks;

namespace Stratis.SmartContracts.CLR.Tests
{
    /// <summary>
    /// A mock-less container for all the parts of contract execution.
    /// Most likely just used to get the VM but saves test rewriting for every change inside execution.
    /// </summary>
    public class ContractExecutorTestContext
    {
        public Network Network { get; }
        public IKeyEncodingStrategy KeyEncodingStrategy { get; }
        public ILoggerFactory LoggerFactory { get; }
        public StateRepositoryRoot State { get; }
        public SmartContractValidator Validator { get; }
        public IAddressGenerator AddressGenerator {get;}
        public ContractAssemblyLoader AssemblyLoader { get; }
        public IContractModuleDefinitionReader ModuleDefinitionReader { get; }
        public IContractPrimitiveSerializer ContractPrimitiveSerializer { get; }
        public IInternalExecutorFactory InternalTxExecutorFactory { get; }
        public IContractAssemblyCache ContractCache { get; }
        public ReflectionVirtualMachine Vm { get; }
        public ISmartContractStateFactory SmartContractStateFactory { get; }
        public StateProcessor StateProcessor { get; }
        public Serializer Serializer { get; }
        public Mock<IEmbeddedContractContainer> mockEmbeddedContractContainer { get; }
        public Mock<IServiceProvider> mockServiceProvider { get; }
        public IPersistenceStrategy PersistenceStrategy { get; }

        public ContractExecutorTestContext(Network network = null)
        {
            this.Network = network ?? new SmartContractsRegTest();
            this.KeyEncodingStrategy = BasicKeyEncodingStrategy.Default;
            this.LoggerFactory = ExtendedLoggerFactory.Create();
            this.State = new StateRepositoryRoot(new NoDeleteSource<byte[], byte[]>(new MemoryDictionarySource()));
            this.PersistenceStrategy = new TestPersistenceStrategy(this.State);
            this.ContractPrimitiveSerializer = new ContractPrimitiveSerializer(this.Network);
            this.Serializer = new Serializer(this.ContractPrimitiveSerializer);
            this.AddressGenerator = new AddressGenerator();
            this.Validator = new SmartContractValidator();
            this.AssemblyLoader = new ContractAssemblyLoader();
            this.ModuleDefinitionReader = new ContractModuleDefinitionReader();
            this.ContractCache = new ContractAssemblyCache();
            this.mockEmbeddedContractContainer = new Mock<IEmbeddedContractContainer>();
            this.mockServiceProvider = new Mock<IServiceProvider>();
            
            {
                var outputType = typeof(Authentication).AssemblyQualifiedName;
                var version = (uint)1;
                this.mockEmbeddedContractContainer.Setup(x => x.TryGetContractTypeAndVersion(new EmbeddedContractAddress(typeof(Authentication), 1), out outputType, out version)).Returns(true);
            }

            {
                var outputType = typeof(MultiSig).AssemblyQualifiedName;
                var version = (uint)1;
                this.mockEmbeddedContractContainer.Setup(x => x.TryGetContractTypeAndVersion(new EmbeddedContractAddress(typeof(MultiSig), 1), out outputType, out version)).Returns(true);
            }

            this.mockEmbeddedContractContainer.Setup(x => x.GetEmbeddedContractAddresses()).Returns(new[] { 
                (uint160)new EmbeddedContractAddress(typeof(Authentication), 1),
                (uint160)new EmbeddedContractAddress(typeof(MultiSig), 1)
            });

            this.mockEmbeddedContractContainer.Setup(x => x.IsActive(It.IsAny<uint160>(), It.IsAny<ChainedHeader>(), It.IsAny<Func<ChainedHeader, int, bool>>())).Returns(true);

            this.mockServiceProvider.Setup(x => x.GetService(It.Is<Type>(t => t == typeof(Network)))).Returns(this.Network);
            this.mockServiceProvider.Setup(x => x.GetService(It.Is<Type>(t => t == typeof(IPersistenceStrategy)))).Returns(this.PersistenceStrategy);
            this.Vm = new EmbeddedContractMachine(this.Validator, this.LoggerFactory, this.AssemblyLoader, this.ModuleDefinitionReader, this.ContractCache, new Mock<ChainIndexer>().Object, null, this.mockServiceProvider.Object,
                this.mockEmbeddedContractContainer.Object);
            this.StateProcessor = new StateProcessor(this.Vm, this.AddressGenerator);
            this.InternalTxExecutorFactory = new InternalExecutorFactory(this.LoggerFactory, this.StateProcessor);
            this.SmartContractStateFactory = new SmartContractStateFactory(this.ContractPrimitiveSerializer, this.InternalTxExecutorFactory, this.Serializer);
        }
    }
}
