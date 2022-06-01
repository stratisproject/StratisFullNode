using System;
using System.Timers;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Settings;

namespace Stratis.Bitcoin.Features.Api
{
    /// <summary>
    /// Configuration related to the API interface.
    /// </summary>
    public class ApiSettings : BaseSettings
    {
        /// <summary>The default port used by the API when the node runs on the Stratis network.</summary>
        public const string DefaultApiHost = "http://localhost";

        /// <summary>URI to node's API interface.</summary>
        [CommandLineOption("apiuri", "URI to node's API interface.")]
        private string ApiHost { get { return this.apiHost ?? (this.UseHttps ? DefaultApiHost.Replace(@"http://", @"https://") : DefaultApiHost); } set { this.apiHost = value; } }
        private string apiHost = null;

        // If a port is set in the -apiuri, it takes precedence over the default port or the port passed in -apiport.
        public Uri ApiUri => new Uri(this.ApiHost.Contains(":") ? this.ApiHost : $"{this.ApiHost}:{this.ApiPort}");

        /// <summary>Port of node's API interface.</summary>
        [CommandLineOption("apiport", "Port of node's API interface.")]
        public int ApiPort { get { return this.apiPort ?? this.nodeSettings.Network.DefaultAPIPort; } set { this.apiPort = value; } }
        private int? apiPort = null;

        /// <summary>URI to node's API interface.</summary>
        [CommandLineOption("keepalive", "Keep Alive interval (set in seconds).")]
        private int KeepAlive { get; set; } = 0;

        public Timer KeepaliveTimer { get; private set; }

        /// <summary>
        /// The HTTPS certificate file path.
        /// </summary>
        /// <remarks>
        /// Password protected certificates are not supported. On MacOs, only p12 certificates can be used without password.
        /// Please refer to .Net Core documentation for usage: <seealso cref="https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate2.-ctor?view=netcore-2.1#System_Security_Cryptography_X509Certificates_X509Certificate2__ctor_System_Byte___" />.
        /// </remarks>
        [CommandLineOption("certificatefilepath", "Path to the certificate used for https traffic encryption. Password protected files are not supported. On MacOs, only p12 certificates can be used without password.", false)]
        public string HttpsCertificateFilePath { get; set; } = null;

        /// <summary>Use HTTPS or not.</summary>
        [CommandLineOption("usehttps", "Use https protocol on the API.")]
        public bool UseHttps { get; set; } = false;

        /// <summary>
        /// Initializes an instance of the object from the node configuration.
        /// </summary>
        /// <param name="nodeSettings">The node configuration.</param>
        public ApiSettings(NodeSettings nodeSettings) : base(nodeSettings)
        {
            if (this.UseHttps && string.IsNullOrWhiteSpace(this.HttpsCertificateFilePath))
                throw new ConfigurationException("The path to a certificate needs to be provided when using https. Please use the argument 'certificatefilepath' to provide it.");

            // Set the keepalive interval (set in seconds).
            if (this.KeepAlive > 0)
            {
                this.KeepaliveTimer = new Timer
                {
                    AutoReset = false,
                    Interval = this.KeepAlive * 1000
                };
            }
        }
    }
}
