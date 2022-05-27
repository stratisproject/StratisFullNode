using System.Net;
using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Stratis.Bitcoin.AsyncWork;

namespace Stratis.Bitcoin.Controllers
{
    /// <summary>
    /// Retrieve stats for the running node
    /// </summary>
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public class DashboardController : Controller
    {
        private readonly IFullNode fullNode;
        private readonly IAsyncProvider asyncProvider;

        public DashboardController(IFullNode fullNode, IAsyncProvider asyncProvider)
        {
            this.fullNode = fullNode;
            this.asyncProvider = asyncProvider;
        }

        /// <summary>
        /// Retrieves the last log output for the node.
        /// </summary>
        /// <returns>Full node log output</returns>
        /// <response code="200">Full node stats returned</response>
        [HttpGet]
        [Route("stats")]
        [Produces(MediaTypeNames.Text.Plain)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.OK)]
        public IActionResult Stats()
        {
            string content = (this.fullNode as FullNode).LastLogOutput;
            return this.Content(content);
        }

        /// <summary>
        /// Retrieves async loop debug data. An async loop is a task that is run on a timer at a fixed interval, optionally with a startup delay.
        /// </summary>
        /// <returns>Async loop debug data</returns>
        /// <response code="200">Async loop stats returned</response>
        [HttpGet]
        [Route("asyncLoopsStats")]
        [Produces(MediaTypeNames.Text.Plain)]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.OK)]
        public IActionResult AsyncLoopsStats()
        {
            return this.Content(this.asyncProvider.GetStatistics(false));
        }
    }
}