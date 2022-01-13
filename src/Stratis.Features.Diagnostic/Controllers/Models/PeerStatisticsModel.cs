using System.Collections.Generic;
using System.Linq;
using Stratis.Features.Diagnostic.PeerDiagnostic;

namespace Stratis.Features.Diagnostic.Controllers.Models
{
    /// <summary>
    /// Records perr statistics.
    /// </summary>
    public class PeerStatisticsModel
    {
        /// <summary>
        /// The peer endpoint.
        /// </summary>
        public string PeerEndPoint { get; set; }

        /// <summary>
        /// Indicates whether the peer is connected.
        /// </summary>
        public bool Connected { get; set; }

        /// <summary>
        /// Indicates whether this is an inbound peer.
        /// </summary>
        public bool Inbound { get; set; }

        /// <summary>
        /// The number of bytes sent to the peer.
        /// </summary>
        public long BytesSent { get; set; }

        /// <summary>
        /// The number of bytes received from the peer.
        /// </summary>
        public long BytesReceived { get; set; }

        /// <summary>
        /// The number of messages received from the peer.
        /// </summary>
        public int ReceivedMessages { get; set; }

        /// <summary>
        /// The number of messages sent to the peer.
        /// </summary>
        public int SentMessages { get; set; }

        /// <summary>
        /// The list of latest events.
        /// </summary>
        public List<string> LatestEvents { get; set; }

        /// <summary>
        /// Class instance constructor.
        /// </summary>
        /// <param name="peer">See <see cref="PeerStatistics"/>.</param>
        /// <param name="connected">Indicates whether the peer is connected.</param>
        public PeerStatisticsModel(PeerStatistics peer, bool connected)
        {
            this.LatestEvents = new List<string>();
            this.Connected = connected;

            if (peer != null)
            {
                this.PeerEndPoint = peer.PeerEndPoint.ToString();
                this.Inbound = peer.Inbound;
                this.BytesReceived = peer.BytesReceived;
                this.BytesSent = peer.BytesSent;
                this.LatestEvents = peer.LatestEvents.ToList();
                this.ReceivedMessages = peer.ReceivedMessages;
                this.SentMessages = peer.SentMessages;
            }
        }
    }
}
