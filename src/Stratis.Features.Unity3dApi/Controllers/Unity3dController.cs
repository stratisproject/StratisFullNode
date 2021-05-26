using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Stratis.Features.Unity3dApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class Unity3dController : Controller
    {
        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;
        
        public Unity3dController(ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        [HttpGet]
        public IActionResult Get()
        {
            return this.Json("API works");
        }

        [HttpGet]
        [Route("status")]
        public IActionResult Status()
        {
            return this.Json("All good");
        }
    }
}
