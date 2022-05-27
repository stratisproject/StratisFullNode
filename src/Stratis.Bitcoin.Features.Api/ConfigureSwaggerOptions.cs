using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using NBitcoin;
using Stratis.Bitcoin.Controllers.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Script = NBitcoin.Script;

namespace Stratis.Bitcoin.Features.Api
{
    /// <summary>
    /// Configures the Swagger generation options.
    /// </summary>
    /// <remarks>This allows API versioning to define a Swagger document per API version after the
    /// <see cref="IApiVersionDescriptionProvider"/> service has been resolved from the service container.
    /// Adapted from https://github.com/microsoft/aspnet-api-versioning/blob/master/samples/aspnetcore/SwaggerSample/ConfigureSwaggerOptions.cs.
    /// </remarks>
    public class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
    {
        private readonly IApiVersionDescriptionProvider provider;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigureSwaggerOptions"/> class.
        /// </summary>
        /// <param name="provider">The <see cref="IApiVersionDescriptionProvider">provider</see> used to generate Swagger documents.</param>
        public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
        {
            this.provider = provider;
        }

        /// <inheritdoc />
        public void Configure(SwaggerGenOptions options)
        {
            // Add a swagger document for each discovered API version
            foreach (ApiVersionDescription description in this.provider.ApiVersionDescriptions)
            {
                options.SwaggerDoc(description.GroupName, CreateInfoForApiVersion(description));
            }

            // Retrieve relevant XML documents via assembly scanning
            var xmlDocuments = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetTypes().Any(t => t.IsSubclassOf(typeof(ControllerBase))))
                .Select(a => Path.Combine(AppContext.BaseDirectory, $"{a.GetName().Name}.xml"));
            
            foreach (string xmlPath in xmlDocuments)
            {
                if (File.Exists(xmlPath))
                {
                    options.IncludeXmlComments(xmlPath, true);
                }
            }
            
            options.CustomSchemaIds(type => type switch
                {
                    // resolve naming clash
                    { } scriptType when scriptType == typeof(Stratis.Bitcoin.Controllers.Models.Script) => "HexEncodedScript",
                    _ => type.ToString()
                });
            
            // map custom types to openapi schema types
            options.MapType<uint256>(() => new OpenApiSchema { Type = "string" });
            options.MapType<Script>(() => new OpenApiSchema { Type = "string" });
            options.MapType<Money>(() => new OpenApiSchema { Type = "int64" });
            
            options.DocumentFilter<CamelCaseRouteFilter>();
            options.DocumentFilter<AlphabeticalTagOrderingFilter>();
            
            options.DescribeAllParametersInCamelCase();
        }

        static OpenApiInfo CreateInfoForApiVersion(ApiVersionDescription description)
        {
            var info = new OpenApiInfo
            {
                Title = "Stratis Node API",
                Version = description.ApiVersion.ToString(),
                Description = "The Stratis Node API allows you to manage and monitor the node, as well as query data from the running network.",
                Contact = new OpenApiContact { Name = "Stratis Platform", Url = new Uri("https://www.stratisplatform.com") },
                License = new OpenApiLicense { Name = "MIT", Url = new Uri("https://opensource.org/licenses/MIT") }
            };

            if (info.Version.Contains("dev"))
            {
                info.Description += " This version of the API is in development and subject to change. Use an earlier version for production applications.";
            }

            if (description.IsDeprecated)
            {
                info.Description += " This API version has been deprecated.";
            }

            return info;
        }
    }
}
