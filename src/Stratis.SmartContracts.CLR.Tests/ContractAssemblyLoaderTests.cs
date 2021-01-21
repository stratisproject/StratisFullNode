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

public class FruitVendor : SmartContract, IStandardToken
{
    public UInt256 TotalSupply => 0;

    public uint Decimals => 0;

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
            string smartContracts130Path = SmartContractLoadContext.GetExactAssembly(new AssemblyName("Stratis.SmartContracts, Version=1.3.0.0"), out _);
            AssemblyLoadContext smartContracts130Ctx = new AssemblyLoadContext(nameof(smartContracts130Ctx));
            Assembly smartContracts130 = smartContracts130Ctx.LoadFromAssemblyPath(smartContracts130Path);

            string smartContractsStandards130Path = SmartContractLoadContext.GetExactAssembly(new AssemblyName("Stratis.SmartContracts.Standards, Version=1.3.0.0"), out _);
            AssemblyLoadContext smartContractsStandards130Ctx = new AssemblyLoadContext(nameof(smartContractsStandards130Ctx));
            Assembly smartContractsStandards130 = smartContractsStandards130Ctx.LoadFromAssemblyPath(smartContractsStandards130Path);

            Assembly Runtime = Assembly.Load("System.Runtime");
            Assembly Core = typeof(object).Assembly;
            HashSet<Assembly> allowedAssemblies = new HashSet<Assembly> {
                Runtime,
                Core,
                smartContracts130,
                typeof(Enumerable).Assembly,
                smartContractsStandards130
            };

            ContractCompilationResult result = ContractCompiler.Compile(this.testContract, allowedAssemblies: allowedAssemblies );
            byte[] bytes = result.Compilation;

            // Test that the node is able to load the futuristic contract.
            ContractAssemblyLoader loader = new ContractAssemblyLoader();
            CSharpFunctionalExtensions.Result<IContractAssembly> result2 = loader.Load(new ContractByteCode(bytes));
            IContractAssembly assembly = result2.Value;
            Assert.Equal("FruitVendor", assembly.Assembly.GetTypes().First().Name);
        }
    }
}
