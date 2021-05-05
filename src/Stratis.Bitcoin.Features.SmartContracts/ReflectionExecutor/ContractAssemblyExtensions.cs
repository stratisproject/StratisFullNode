using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Stratis.SmartContracts.CLR.Loader;

namespace Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor
{
    public static class ContractAssemblyExtensions
    {
        /// <summary>
        /// Gets the public methods defined by the contract, ignoring property getters/setters.
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<MethodInfo> GetPublicMethods(this IContractAssembly contractAssembly)
        {
            Type deployedType = contractAssembly.DeployedType;

            if (deployedType == null)
                return new List<MethodInfo>();

            return deployedType
                .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance) // Get only the methods declared on the contract type
                .Where(m => !m.IsSpecialName); // Ignore property setters/getters
        }

        public static IEnumerable<PropertyInfo> GetPublicGetterProperties(this IContractAssembly contractAssembly)
        {
            Type deployedType = contractAssembly.DeployedType;

            if (deployedType == null)
                return new List<PropertyInfo>();

            return deployedType
                .GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetGetMethod() != null);
        }
    }
}
