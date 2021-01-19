using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using CSharpFunctionalExtensions;

namespace Stratis.SmartContracts.CLR.Loader
{
    public class SmartContractLoadContext : AssemblyLoadContext
    {
        private AssemblyLoadContext defaultContext;

        public SmartContractLoadContext(AssemblyLoadContext defaultContext)
        {
            this.defaultContext = defaultContext;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            // Ensure that an exact compatible version is used.
            if (assemblyName.Name == "Stratis.SmartContracts.Standards" && assemblyName.Version.Major == 1 && assemblyName.Version.Minor < 4)
                return this.LoadFromAssemblyPath(Path.GetFullPath("Stratis.SmartContracts.Standards (64 bit).dll"));

            return this.defaultContext.LoadFromAssemblyName(assemblyName);
        }
    }

    /// <summary>
    /// Loads assemblies from bytecode.
    /// </summary>
    public class ContractAssemblyLoader : ILoader
    {
        /// <summary>
        /// Loads a contract from a raw byte array into a custom <see cref="AssemblyLoadContext"/>.
        /// </summary>
        public Result<IContractAssembly> Load(ContractByteCode bytes)
        {
            // Assembly.Load(byte[]) loads the assembly into a custom AssemblyLoadContext
            try
            {
                SmartContractLoadContext context = new SmartContractLoadContext(AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly()));

                MemoryStream s = new MemoryStream(bytes.Value);
                Assembly assembly = context.LoadFromStream(s);

                return Result.Ok<IContractAssembly>(new ContractAssembly(assembly));
            }
            catch (BadImageFormatException e)
            {
                return Result.Fail<IContractAssembly>(e.Message);
            }
        }
    }
}
