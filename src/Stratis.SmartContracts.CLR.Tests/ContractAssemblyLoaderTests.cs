using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Stratis.SmartContracts.CLR.Compilation;
using Stratis.SmartContracts.CLR.Loader;
using Xunit;

namespace Stratis.SmartContracts.CLR.Tests
{
    public class ContractAssemblyLoaderTests
    {
        string testContract = @"
using Stratis.SmartContracts;
using Stratis.SmartContracts.Standards;
public class FruitVendor : SmartContract, IStandardToken256
{
    public UInt256 TotalSupply => 0;
    public byte Decimals => 0;
    public FruitVendor(ISmartContractState state) : base(state)
    {
    }
    public UInt256 GetBalance(Address address)
    {
        return 0;
    }
    public bool TransferTo(Address address, UInt256 amount)
    {
        return true;
    }
    public bool TransferFrom(Address from, Address to, UInt256 amount)
    {
        return true;
    }
    public bool Approve(Address spender, UInt256 currentAmount, UInt256 amount)
    {
        return true;
    }
    public UInt256 Allowance(Address owner, Address spender)
    {
        return 0;
    }
}
        ";

        [Fact]
        public void ContractAssemblyLoaderIsForwardCompatibleWithSmartContractAndStandardsUpdates()
        {
            // Create the byte code of a contract that contains new data types that are not (normally) supported by the current node.
            ContractCompilationResult result = ContractCompiler.Compile(this.testContract);
            byte[] bytes = result.Compilation;

            // Test that the node is able to load the futuristic contract.
            ContractAssemblyLoader loader = new ContractAssemblyLoader();
            CSharpFunctionalExtensions.Result<IContractAssembly> result2 = loader.Load(new ContractByteCode(bytes));
            IContractAssembly assembly = result2.Value;
            Assert.Equal("FruitVendor", assembly.Assembly.GetTypes().First().Name);
        }
    }
}