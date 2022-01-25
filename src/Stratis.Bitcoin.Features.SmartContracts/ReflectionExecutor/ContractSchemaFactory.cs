using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.OpenApi.Models;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR.Loader;

namespace Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor
{
    /// <summary>
    /// Factory for generating swagger schema for smart contract primitives.
    /// </summary>
    public class ContractSchemaFactory
    {
        public static readonly Dictionary<Type, Func<OpenApiSchema>> PrimitiveTypeMap = new Dictionary<Type, Func<OpenApiSchema>>
        {
            { typeof(short), () => new OpenApiSchema { Type = "integer", Format = "int32" } },
            { typeof(ushort), () => new OpenApiSchema { Type = "integer", Format = "int32" } },
            { typeof(int), () => new OpenApiSchema { Type = "integer", Format = "int32" } },
            { typeof(uint), () => new OpenApiSchema { Type = "integer", Format = "int32" } },
            { typeof(long), () => new OpenApiSchema { Type = "integer", Format = "int64" } },
            { typeof(ulong), () => new OpenApiSchema { Type = "integer", Format = "int64" } },
            { typeof(float), () => new OpenApiSchema { Type = "number", Format = "float" } },
            { typeof(double), () => new OpenApiSchema { Type = "number", Format = "double" } },
            { typeof(decimal), () => new OpenApiSchema { Type = "number", Format = "double" } },
            { typeof(byte), () => new OpenApiSchema { Type = "integer", Format = "int32" } },
            { typeof(sbyte), () => new OpenApiSchema { Type = "integer", Format = "int32" } },
            { typeof(byte[]), () => new OpenApiSchema { Type = "string", Format = "byte" } },
            { typeof(sbyte[]), () => new OpenApiSchema { Type = "string", Format = "byte" } },
            { typeof(char), () => new OpenApiSchema { Type = "string", Format = "char" } },
            { typeof(string), () => new OpenApiSchema { Type = "string" } },
            { typeof(bool), () => new OpenApiSchema { Type = "boolean" } },
            { typeof(Address), () => new OpenApiSchema { Type = "string" } }
        };

        /// <summary>
        /// Maps a contract assembly to its schemas.
        /// </summary>
        /// <param name="assembly"></param>
        /// <returns></returns>
        public IDictionary<string, OpenApiSchema> Map(IContractAssembly assembly)
        {
            return this.Map(assembly.GetPublicMethods());
        }

        /// <summary>
        /// Maps a type to its schemas.
        /// </summary>
        /// <param name="methods"></param>
        /// <returns></returns>
        public IDictionary<string, OpenApiSchema> Map(IEnumerable<MethodInfo> methods)
        {
            return methods.Select(this.Map).ToDictionary(k => k.Title, v => v);
        }

        /// <summary>
        /// Maps a single method to a schema.
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        public OpenApiSchema Map(MethodInfo method)
        {
            var schema = new OpenApiSchema();
            schema.Properties = new Dictionary<string, OpenApiSchema>();
            schema.Title = method.Name;

            foreach (ParameterInfo parameter in method.GetParameters())
            {
                // Default to string.
                OpenApiSchema paramSchema = PrimitiveTypeMap.ContainsKey(parameter.ParameterType)
                    ? PrimitiveTypeMap[parameter.ParameterType]()
                    : PrimitiveTypeMap[typeof(string)]();

                schema.Properties.Add(parameter.Name, paramSchema);
            }

            return schema;
        }
    }
}