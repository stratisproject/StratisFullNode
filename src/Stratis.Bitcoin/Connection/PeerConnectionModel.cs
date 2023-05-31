using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Connection
{
    public class PeerConnectionModel
    {
        /// <summary>
        /// The IP address and port of the peer.
        /// </summary>
        [JsonProperty(PropertyName = "address")]
        public string Address { get; internal set; }

        /// <summary>
        /// The user agent this node sends in its version message.
        /// </summary>
        [JsonProperty(PropertyName = "subversion")]
        public string SubVersion { get; internal set; }

        /// <summary>
        /// Whether node is inbound or outbound connection.
        /// </summary>
        [JsonProperty(PropertyName = "inbound")]
        public bool Inbound { get; internal set; }

        [JsonProperty(PropertyName = "height")]
        public int Height { get; internal set; }
    }
}
