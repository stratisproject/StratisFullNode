using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Loader;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;

namespace Stratis.SmartContracts.CLR.Loader
{
    /// <summary>
    /// Loads assemblies from bytecode.
    /// </summary>
    /// <para>
    /// TODO this may return cached assemblies in the future.
    /// </para>
    public class ContractAssemblyLoader : ILoader
    {
        /// <summary>
        /// Loads a contract from a raw byte array into an anonymous <see cref="AssemblyLoadContext"/>.
        /// </summary>
        public Result<IContractAssembly> Load(ContractByteCode bytes)
        {
            // Assembly.Load(byte[]) loads the assembly into a new anonymous AssemblyLoadContext
            // TODO in the future, we will use a custom AssemblyLoadContext
            try
            {
                Assembly assembly = Assembly.Load(bytes.Value);

                return Result.Ok<IContractAssembly>(new ContractAssembly(assembly));
            }
            catch (BadImageFormatException e)
            {
                return Result.Fail<IContractAssembly>(e.Message);
            }
        }
    }
}