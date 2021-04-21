using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.OpenApi.Models;
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

            var schemaRepository = new SchemaRepository(documentName);

            var swaggerDoc = new OpenApiDocument
            {
                Info = info,
                Servers = GenerateServers(host, basePath),
                Paths = GeneratePaths(null, schemaRepository),
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

        private OpenApiPaths GeneratePaths(object applicableApiDescriptions, SchemaRepository schemaRepository)
        {
            throw new NotImplementedException();
        }
    }
}
