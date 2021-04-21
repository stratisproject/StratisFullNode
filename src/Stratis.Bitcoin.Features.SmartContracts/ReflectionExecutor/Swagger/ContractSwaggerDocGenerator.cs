using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor.Consensus.Rules;
using Stratis.SmartContracts.CLR.Loader;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor.Swagger
{
    /// <summary>
    /// Creates swagger documents for a contract assembly.
    /// Maps the methods of a contract and its parameters to a call endpoint.
    /// Maps the properties of a contract to an local call endpoint.
    /// </summary>
    public class ContractSwaggerDocGenerator : ISwaggerProvider
    {
        private readonly SwaggerGeneratorOptions options;
        private readonly string address;
        private readonly IContractAssembly assembly;
        private readonly string defaultWalletName;
        private readonly string defaultSenderAddress;

        public ContractSwaggerDocGenerator(SwaggerGeneratorOptions options, string address, IContractAssembly assembly, string defaultWalletName = "", string defaultSenderAddress = "")
        {
            this.options = options;
            this.address = address;
            this.assembly = assembly;
            this.defaultWalletName = defaultWalletName;
            this.defaultSenderAddress = defaultSenderAddress;
        }

        private IDictionary<string, OpenApiSchema> CreateDefinitions()
        {
            // Creates schema for each of the methods in the contract.
            var schemaFactory = new ContractSchemaFactory();

            return schemaFactory.Map(this.assembly);
        }

        private IDictionary<string, OpenApiPathItem> CreatePathItems(IDictionary<string, OpenApiSchema> schema)
        {
            // Creates path items for each of the methods & properties in the contract + their schema.O

            IEnumerable<MethodInfo> methods = this.assembly.GetPublicMethods();

            var methodPaths = methods
                .ToDictionary(k => $"/api/contract/{this.address}/method/{k.Name}", v => this.CreatePathItem(v, schema));

            IEnumerable<PropertyInfo> properties = this.assembly.GetPublicGetterProperties();

            var propertyPaths = properties
                .ToDictionary(k => $"/api/contract/{this.address}/property/{k.Name}", v => this.CreatePathItem(v));

            foreach (KeyValuePair<string, OpenApiPathItem> item in propertyPaths)
            {
                methodPaths[item.Key] = item.Value;
            }

            return methodPaths;
        }

        private OpenApiPathItem CreatePathItem(PropertyInfo propertyInfo)
        {
            var operation = new OpenApiOperation
            {
                Tags = new List<OpenApiTag> { new OpenApiTag { Name = propertyInfo.Name } },
                OperationId = propertyInfo.Name,
                Parameters = this.GetLocalCallMetadataHeaderParams(),
                Responses = new OpenApiResponses { { "200", new OpenApiResponse { Description = "Success" } } }
            };

            var pathItem = new OpenApiPathItem
            {
                Operations = new Dictionary<OperationType, OpenApiOperation> { { OperationType.Get, operation } }
            };

            return pathItem;
        }

        private OpenApiPathItem CreatePathItem(MethodInfo methodInfo, IDictionary<string, OpenApiSchema> schema)
        {
            var operation = new OpenApiOperation
            {
                Tags = new List<OpenApiTag> { new OpenApiTag { Name = methodInfo.Name } },
                OperationId = methodInfo.Name,
                Parameters = this.GetCallMetadataHeaderParams(),
                Responses = new OpenApiResponses { { "200", new OpenApiResponse { Description = "Success" } } }
            };

            operation.RequestBody = new OpenApiRequestBody
            {
                Description = $"{methodInfo.Name}",
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    { "application/json", new OpenApiMediaType
                        {
                            Schema = schema[methodInfo.Name]
                        }
                    }
                },
            };

            var pathItem = new OpenApiPathItem
            {
                Operations = new Dictionary<OperationType, OpenApiOperation> { { OperationType.Post, operation } }
            };

            return pathItem;
        }

        private List<OpenApiParameter> GetLocalCallMetadataHeaderParams()
        {
            return new List<OpenApiParameter>
            {
                new OpenApiParameter
                {
                    Name = "GasPrice",
                    In = ParameterLocation.Header,
                    Required = true,
                    Schema = new OpenApiSchema
                    {
                        Type = "number",
                        Format = "int64",
                        Minimum = SmartContractFormatLogic.GasPriceMinimum,
                        Maximum = SmartContractFormatLogic.GasPriceMaximum,
                        Default = new OpenApiLong((long)SmartContractMempoolValidator.MinGasPrice) // Long not ideal but there's no OpenApiUlong
                    },
                },
                new OpenApiParameter
                {
                    Name = "GasLimit",
                    In = ParameterLocation.Header,
                    Required = true,
                    Schema = new OpenApiSchema
                    {
                        Type = "number",
                        Format = "int64",
                        Minimum = SmartContractFormatLogic.GasLimitCallMinimum,
                        Maximum = SmartContractFormatLogic.GasLimitMaximum,
                        Default = new OpenApiLong((long)SmartContractFormatLogic.GasLimitMaximum) // Long not ideal but there's no OpenApiUlong
                    },
                },
                new OpenApiParameter
                {
                    Name = "Amount",
                    In = ParameterLocation.Header,
                    Required = true,
                    Schema = new OpenApiSchema
                    {
                        Type = "string",
                        Default = new OpenApiString("0")
                    },
                },
                new OpenApiParameter
                {
                    Name = "Sender",
                    In = ParameterLocation.Header,
                    Required = true,
                    Schema = new OpenApiSchema
                    {
                        Type = "string",
                        Default = new OpenApiString(this.defaultSenderAddress)
                    },
                }
            };
        }

        private List<OpenApiParameter> GetCallMetadataHeaderParams()
        {
            return new List<OpenApiParameter>
            {
                new OpenApiParameter
                {
                    Name = "GasPrice",
                    In = ParameterLocation.Header,
                    Required = true,
                    Schema = new OpenApiSchema
                    {
                        Type = "number",
                        Format = "int64",
                        Minimum = SmartContractFormatLogic.GasPriceMinimum,
                        Maximum = SmartContractFormatLogic.GasPriceMaximum,
                        Default = new OpenApiLong((long)SmartContractMempoolValidator.MinGasPrice) // Long not ideal but there's no OpenApiUlong
                    },
                },
                new OpenApiParameter
                {
                    Name = "GasLimit",
                    In = ParameterLocation.Header,
                    Required = true,
                    Schema = new OpenApiSchema
                    {
                        Type = "number",
                        Format = "int64",
                        Minimum = SmartContractFormatLogic.GasLimitCallMinimum,
                        Maximum = SmartContractFormatLogic.GasLimitMaximum,
                        Default = new OpenApiLong((long)SmartContractFormatLogic.GasLimitCallMinimum) // Long not ideal but there's no OpenApiUlong
                    },
                },
                new OpenApiParameter
                {
                    Name = "Amount",
                    In = ParameterLocation.Header,
                    Required = true,
                    Schema = new OpenApiSchema
                    {
                        Type = "string",
                        Default = new OpenApiString("0")
                    },
                },
                new OpenApiParameter
                {
                    Name = "FeeAmount",
                    In = ParameterLocation.Header,
                    Required = true,
                    Schema = new OpenApiSchema
                    {
                        Type = "string",
                        Default = new OpenApiString("0.01")
                    },
                },
                new OpenApiParameter
                {
                    Name = "WalletName",
                    In = ParameterLocation.Header,
                    Required = true,
                    Schema = new OpenApiSchema
                    {
                        Type = "string",
                        Default = new OpenApiString(this.defaultWalletName)
                    },
                },
                new OpenApiParameter
                {
                    Name = "WalletPassword",
                    In = ParameterLocation.Header,
                    Required = true,
                    Schema = new OpenApiSchema
                    {
                        Type = "string"
                    }
                },
                new OpenApiParameter
                {
                    Name = "Sender",
                    In = ParameterLocation.Header,
                    Required = true,
                    Schema = new OpenApiSchema
                    {
                        Type = "string",
                        Default = new OpenApiString(this.defaultSenderAddress)
                    },
                }
            };
        }

        /// <summary>
        /// Generates a swagger document for an assembly. Adds a path per public method, with a request body
        /// that contains the parameters of the method. Transaction-related metadata is added to header fields
        /// which are pre-filled with sensible defaults.
        /// </summary>
        /// <param name="documentName">The name of the swagger document to use.</param>
        /// <param name="host"></param>
        /// <param name="basePath"></param>
        /// <param name="schemes"></param>
        /// <returns></returns>
        public OpenApiDocument GetSwagger(string documentName, string host = null, string basePath = null)
        {
            if (!this.options.SwaggerDocs.TryGetValue(documentName, out OpenApiInfo info))
                throw new UnknownSwaggerDocument(documentName, this.options.SwaggerDocs.Select(d => d.Key));

            SetInfo(info);

            IDictionary<string, OpenApiSchema> definitions = this.CreateDefinitions();

            var swaggerDoc = new OpenApiDocument
            {
                Info = info,
                Servers = GenerateServers(host, basePath),
                Paths = GeneratePaths(definitions),
                Components = new OpenApiComponents
                {
                    Schemas = null,
                    SecuritySchemes = new Dictionary<string, OpenApiSecurityScheme>(this.options.SecuritySchemes)
                },
                SecurityRequirements = new List<OpenApiSecurityRequirement>(this.options.SecurityRequirements)
            };

            return swaggerDoc;
        }

        private void SetInfo(OpenApiInfo info)
        {
            info.Title = $"{this.assembly.DeployedType.Name} Contract API";
            info.Description = $"{this.address}";
        }

        private IList<OpenApiServer> GenerateServers(string host, string basePath)
        {
            if (this.options.Servers.Any())
            {
                return new List<OpenApiServer>(this.options.Servers);
            }

            return (host == null && basePath == null)
                ? new List<OpenApiServer>()
                : new List<OpenApiServer> { new OpenApiServer { Url = $"{host}{basePath}" } };
        }

        private OpenApiPaths GeneratePaths(IDictionary<string, OpenApiSchema> definitions)
        {
            IDictionary<string, OpenApiPathItem> paths = this.CreatePathItems(definitions);

            OpenApiPaths pathsObject = new OpenApiPaths();

            foreach (KeyValuePair<string, OpenApiPathItem> path in paths)
            {
                pathsObject.Add(path.Key, path.Value);
            }

            return pathsObject;
        }
    }
}
