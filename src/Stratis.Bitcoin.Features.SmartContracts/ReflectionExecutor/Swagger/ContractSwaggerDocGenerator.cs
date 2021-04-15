using System;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;

namespace Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor.Swagger
{
    public class ContractSwaggerDocGenerator : ISwaggerProvider
    {
        public OpenApiDocument GetSwagger(string documentName, string host = null, string basePath = null)
        {
            throw new NotImplementedException();
        }
    }
}
