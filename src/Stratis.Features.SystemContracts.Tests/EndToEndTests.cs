using System.Collections.Generic;
using System.Linq;
using System.Text;
using Stratis.Bitcoin.Networks;
using Stratis.Features.SystemContracts.Contracts;
using Stratis.Patricia;
using Stratis.SmartContracts.Core.State;
using Xunit;

namespace Stratis.Features.SystemContracts.Tests
{
    public class EndToEndTests
    {
        [Fact]
        public void Complete_Execution_Success()
        {
            var network = new StraxMain();
            var authContract = new AuthContract.Dispatcher(network.SystemContractContainer);
            var dataStorageContract = new DataStorageContract.Dispatcher(network, authContract);

            var dispatchers = new List<IDispatcher>
            {
                authContract,
                dataStorageContract
            };

            var dispatcherRegistry = new DispatcherRegistry(dispatchers);
            var runner = new SystemContractRunner(dispatcherRegistry);

            var state = new StateRepositoryRoot(new MemoryDictionarySource());

            var initialRoot = state.Root.ToArray();

            var key = "Key";
            var value = "Value";
            var @params = new object[]
            {
                new string[] { "secret" },
                key,
                value
            };

            var callData = new SystemContractCall(DataStorageContract.Identifier, nameof(DataStorageContract.AddData), @params, 1);

            var context = new SystemContractTransactionContext(state, null /* This isn't used anywhere at the moment */, callData);

            ISystemContractRunnerResult result = runner.Execute(context);

            Assert.Equal(true, result.Result);

            // State has changed
            Assert.False(initialRoot.SequenceEqual(state.Root));

            // Query the state directly.
            var storedData = state.GetStorageValue(DataStorageContract.Identifier.Data, Encoding.UTF8.GetBytes(key));
            Assert.Equal(value, Encoding.UTF8.GetString(storedData));
        }

        [Fact]
        public void Complete_Execution_Fails()
        {
            var network = new StraxMain();
            var authContract = new AuthContract.Dispatcher(network.SystemContractContainer);
            var dataStorageContract = new DataStorageContract.Dispatcher(network, authContract);

            var dispatchers = new List<IDispatcher>
            {
                authContract,
                dataStorageContract
            };

            var dispatcherRegistry = new DispatcherRegistry(dispatchers);
            var runner = new SystemContractRunner(dispatcherRegistry);

            var state = new StateRepositoryRoot(new MemoryDictionarySource());

            var initialRoot = state.Root.ToArray();

            var @params = new object[] {};

            var callData = new SystemContractCall(DataStorageContract.Identifier, "MethodThatDoesntExist", @params, 1);

            var context = new SystemContractTransactionContext(state, null /* This isn't used anywhere at the moment */, callData);

            ISystemContractRunnerResult result = runner.Execute(context);

            Assert.Null(result.Result);

            // State has not changed
            Assert.True(initialRoot.SequenceEqual(state.Root));
        }
    }
}
