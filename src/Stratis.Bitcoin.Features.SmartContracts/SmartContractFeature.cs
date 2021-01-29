using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Policy;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.MemoryPool.Interfaces;
using Stratis.Bitcoin.Features.Miner;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.SmartContracts.Caching;
using Stratis.Bitcoin.Features.SmartContracts.Interop;
using Stratis.Bitcoin.Features.SmartContracts.Interop.EthereumClient;
using Stratis.Bitcoin.Features.SmartContracts.PoA;
using Stratis.Bitcoin.Features.SmartContracts.PoW;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Caching;
using Stratis.SmartContracts.CLR.Compilation;
using Stratis.SmartContracts.CLR.Decompilation;
using Stratis.SmartContracts.CLR.Loader;
using Stratis.SmartContracts.CLR.Local;
using Stratis.SmartContracts.CLR.ResultProcessors;
using Stratis.SmartContracts.CLR.Serialization;
using Stratis.SmartContracts.CLR.Validation;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.Interfaces;
using Stratis.SmartContracts.Core.Receipts;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.Core.Util;

namespace Stratis.Bitcoin.Features.SmartContracts
{
    public sealed class SmartContractFeature : FullNodeFeature
    {
        private readonly IConsensusManager consensusManager;
        private readonly ILogger logger;
        private readonly Network network;
        private readonly IStateRepositoryRoot stateRoot;
        private readonly InteropPoller interopPoller;

        public SmartContractFeature(IConsensusManager consensusLoop, ILoggerFactory loggerFactory, Network network, IStateRepositoryRoot stateRoot, InteropPoller interopPoller)
        {
            this.consensusManager = consensusLoop;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.network = network;
            this.stateRoot = stateRoot;
            this.interopPoller = interopPoller;
        }

        public override Task InitializeAsync()
        {
            // TODO: This check should be more robust
            Guard.Assert(this.network.Consensus.ConsensusFactory is SmartContractPowConsensusFactory
                         || this.network.Consensus.ConsensusFactory is SmartContractPoAConsensusFactory
                         || this.network.Consensus.ConsensusFactory is SmartContractCollateralPoAConsensusFactory);

            this.stateRoot.SyncToRoot(((ISmartContractBlockHeader)this.consensusManager.Tip.Header).HashStateRoot.ToBytes());

            this.interopPoller?.Initialize();

            this.logger.LogInformation("Smart Contract Feature Injected.");
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            this.interopPoller?.Dispose();
        }
    }

    public class SmartContractOptions
    {
        public SmartContractOptions(IServiceCollection services, Network network)
        {
            this.Services = services;
            this.Network = network;
        }

        public IServiceCollection Services { get; }
        public Network Network { get; }
    }

    public static partial class IFullNodeBuilderExtensions
    {
        /// <summary>
        /// Adds the smart contract feature to the node.
        /// </summary>
        public static IFullNodeBuilder AddSmartContracts(this IFullNodeBuilder fullNodeBuilder, Action<SmartContractOptions> options = null, Action<SmartContractOptions> preOptions = null)
        {
            LoggingConfiguration.RegisterFeatureNamespace<SmartContractFeature>("smartcontracts");

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<SmartContractFeature>()
                    .FeatureServices(services =>
                    {
                        // Before setting up, invoke any additional options.
                        preOptions?.Invoke(new SmartContractOptions(services, fullNodeBuilder.Network));

                        // STATE ----------------------------------------------------------------------------
                        services.AddSingleton<DBreezeContractStateStore>();
                        services.AddSingleton<NoDeleteContractStateSource>();
                        services.AddSingleton<IStateRepositoryRoot, StateRepositoryRoot>();

                        // CONSENSUS ------------------------------------------------------------------------
                        services.Replace(ServiceDescriptor.Singleton<IMempoolValidator, SmartContractMempoolValidator>());
                        services.AddSingleton<StandardTransactionPolicy, SmartContractTransactionPolicy>();

                        // CONTRACT EXECUTION ---------------------------------------------------------------
                        services.AddSingleton<IInternalExecutorFactory, InternalExecutorFactory>();
                        services.AddSingleton<IContractAssemblyCache, ContractAssemblyCache>();
                        services.AddSingleton<IVirtualMachine, ReflectionVirtualMachine>();
                        services.AddSingleton<IAddressGenerator, AddressGenerator>();
                        services.AddSingleton<ILoader, ContractAssemblyLoader>();
                        services.AddSingleton<IContractModuleDefinitionReader, ContractModuleDefinitionReader>();
                        services.AddSingleton<IStateFactory, StateFactory>();
                        services.AddSingleton<SmartContractTransactionPolicy>();
                        services.AddSingleton<IStateProcessor, StateProcessor>();
                        services.AddSingleton<ISmartContractStateFactory, SmartContractStateFactory>();
                        services.AddSingleton<ILocalExecutor, LocalExecutor>();
                        services.AddSingleton<IBlockExecutionResultCache, BlockExecutionResultCache>();

                        // RECEIPTS -------------------------------------------------------------------------
                        services.AddSingleton<IReceiptRepository, PersistentReceiptRepository>();

                        // UTILS ----------------------------------------------------------------------------
                        services.AddSingleton<ISenderRetriever, SenderRetriever>();

                        services.AddSingleton<IMethodParameterSerializer, MethodParameterByteSerializer>();
                        services.AddSingleton<IMethodParameterStringSerializer, MethodParameterStringSerializer>();
                        services.AddSingleton<ICallDataSerializer, CallDataSerializer>();

                        // Registers the ScriptAddressReader concrete type and replaces the IScriptAddressReader implementation
                        // with SmartContractScriptAddressReader, which depends on the ScriptAddressReader concrete type.
                        services.AddSingleton<ScriptAddressReader>();
                        services.Replace(new ServiceDescriptor(typeof(IScriptAddressReader), typeof(SmartContractScriptAddressReader), ServiceLifetime.Singleton));

                        // After setting up, invoke any additional options which can replace services as required.
                        options?.Invoke(new SmartContractOptions(services, fullNodeBuilder.Network));
                    });
            });

            return fullNodeBuilder;
        }

        public static IFullNodeBuilder AddInteroperability(this IFullNodeBuilder fullNodeBuilder)
        {
            fullNodeBuilder.ConfigureFeature(features =>
            {
                IFeatureRegistration feature = fullNodeBuilder.Features.FeatureRegistrations.FirstOrDefault(f => f.FeatureType == typeof(SmartContractFeature));
                feature.FeatureServices(services =>
                {
                    services.AddSingleton<InteropSettings>();
                    services.AddSingleton<IEthereumClientBase, EthereumClientBase>();
                    services.AddSingleton<IInteropRequestRepository, InteropRequestRepository>();
                    services.AddSingleton<IInteropRequestKeyValueStore, InteropRequestKeyValueStore>();
                    services.AddSingleton<IConversionRequestKeyValueStore, ConversionRequestKeyValueStore>();
                    services.AddSingleton<IConversionRequestRepository, ConversionRequestRepository>();
                    services.AddSingleton<IInteropTransactionManager, InteropTransactionManager>();
                    services.AddSingleton<InteropPoller>();
                });
            });

            return fullNodeBuilder;
        }

        /// <summary>Adds Proof-of-Authority mining to the side chain node.</summary>
        /// <typeparam name="T">The type of block definition to use.</typeparam>
        public static IFullNodeBuilder AddPoAMiningCapability<T>(this IFullNodeBuilder fullNodeBuilder) where T : BlockDefinition
        {
            fullNodeBuilder.ConfigureFeature(features =>
            {
                IFeatureRegistration feature = fullNodeBuilder.Features.FeatureRegistrations.FirstOrDefault(f => f.FeatureType == typeof(PoAFeature));
                feature.FeatureServices(services =>
                {
                    services.AddSingleton<IPoAMiner, PoAMiner>();
                    services.AddSingleton<MinerSettings>();
                    services.AddSingleton<PoAMinerSettings>();
                    services.AddSingleton<BlockDefinition, T>();
                    services.AddSingleton<IBlockBufferGenerator, BlockBufferGenerator>();
                });
            });

            return fullNodeBuilder;
        }

        /// <summary>
        /// This node will be configured with the reflection contract executor.
        /// <para>
        /// Should we require another executor, we will need to create a separate daemon and network.
        /// </para>
        /// </summary>
        public static SmartContractOptions UseReflectionExecutor(this SmartContractOptions options)
        {
            IServiceCollection services = options.Services;

            // Validator
            services.AddSingleton<ISmartContractValidator, SmartContractValidator>();

            // Executor et al.
            services.AddSingleton<IContractRefundProcessor, ContractRefundProcessor>();
            services.AddSingleton<IContractTransferProcessor, ContractTransferProcessor>();
            services.AddSingleton<IKeyEncodingStrategy, BasicKeyEncodingStrategy>();
            services.AddSingleton<IContractExecutorFactory, ReflectionExecutorFactory>();
            services.AddSingleton<IMethodParameterSerializer, MethodParameterByteSerializer>();
            services.AddSingleton<IContractPrimitiveSerializer, ContractPrimitiveSerializer>();
            services.AddSingleton<ISerializer, Serializer>();

            // Controllers + utils
            services.AddSingleton<CSharpContractDecompiler>();

            return options;
        }
    }
}
