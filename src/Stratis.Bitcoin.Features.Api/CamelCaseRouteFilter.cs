using System;
using System.Linq;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Stratis.Bitcoin.Features.Api
{
    /// <summary>
    /// Converts PascalCase route parameters to camelCase
    /// </summary>
    internal class CamelCaseRouteFilter : IDocumentFilter
    {
        public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
        {
            var paths = swaggerDoc.Paths.ToDictionary(entry =>
                {
                    var pathParts = entry.Key.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    var camelCasePathParts = pathParts.Select(
                        part => part.StartsWith('{') || part.All(char.IsUpper)
                            ? part
                            : char.ToLowerInvariant(part[0]) + part[1..]);
                    return $"/{string.Join('/', camelCasePathParts)}";
                },
                entry => entry.Value);
            
            swaggerDoc.Paths = new OpenApiPaths();
            foreach ((string key, OpenApiPathItem value) in paths)
            {
                swaggerDoc.Paths.Add(key, value);
            }
        }
    }
}