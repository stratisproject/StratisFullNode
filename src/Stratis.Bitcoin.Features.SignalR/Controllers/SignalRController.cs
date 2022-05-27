using System.Net;
using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.SignalR.Controllers
{
    /// <summary>
    /// Retrieve SignalR connection settings
    /// </summary>
    [Route("api/[controller]")]
    public class SignalRController : Controller
    {
        private readonly SignalRSettings signalRSettings;

        public SignalRController(SignalRSettings signalRSettings)
        {
            Guard.NotNull(signalRSettings, nameof(signalRSettings));

            this.signalRSettings = signalRSettings;
        }

        /// <summary>
        /// Returns SignalR Connection Info.
        /// </summary>
        /// <returns>Returns SignalR Connection Info as Json {SignalRUri,SignalRPort}</returns>
        /// <response code="200">Returns connection info</response>
        [Route("getConnectionInfo")]
        [HttpGet]
        [Produces(MediaTypeNames.Application.Json)]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        public IActionResult GetConnectionInfo()
        {
            return this.Json(new
            {
                this.signalRSettings.SignalRUri,
                this.signalRSettings.SignalRPort
            });
        }
    }
}
