using System;
using System.Text;
using System.Timers;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Features.Unity3dApi
{
    public class Unity3dApiSettings
    {
        /// <summary>The default port used by the API when the node runs on the Stratis network.</summary>
        public const string DefaultApiHost = "http://localhost";

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;
        
        public bool EnableUnityAPI { get; set; }

        /// <summary>URI to node's API interface.</summary>
        public Uri ApiUri { get; set; }

        /// <summary>Port of node's API interface.</summary>
        public int ApiPort { get; set; }

        /// <summary>URI to node's API interface.</summary>
        public Timer KeepaliveTimer { get; private set; }

        /// <summary>
        /// Port on which to listen for incoming API connections.
        /// </summary>
        public int DefaultAPIPort { get; protected set; } = 44336;

        /// <summary>
        /// The HTTPS certificate file path.
        /// </summary>
        /// <remarks>
        /// Password protected certificates are not supported. On MacOs, only p12 certificates can be used without password.
        /// Please refer to .Net Core documentation for usage: <seealso cref="https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate2.-ctor?view=netcore-2.1#System_Security_Cryptography_X509Certificates_X509Certificate2__ctor_System_Byte___" />.
        /// </remarks>
        public string HttpsCertificateFilePath { get; set; }

        /// <summary>Use HTTPS or not.</summary>
        public bool UseHttps { get; set; }

        /// <summary>
        /// Initializes an instance of the object from the node configuration.
        /// </summary>
        /// <param name="nodeSettings">The node configuration.</param>
        public Unity3dApiSettings(NodeSettings nodeSettings)
        {
            Guard.NotNull(nodeSettings, nameof(nodeSettings));

            this.logger = nodeSettings.LoggerFactory.CreateLogger(typeof(Unity3dApiSettings).FullName);
            TextFileConfiguration config = nodeSettings.ConfigReader;

            this.EnableUnityAPI = config.GetOrDefault("unityapi_enable", false);

            if (!this.EnableUnityAPI)
            {
                this.logger.LogDebug("Unity API disabled.");
                return;
            }

            this.UseHttps = config.GetOrDefault("unityapi_usehttps", false);
            this.HttpsCertificateFilePath = config.GetOrDefault("unityapi_certificatefilepath", (string)null);

            if (this.UseHttps && string.IsNullOrWhiteSpace(this.HttpsCertificateFilePath))
                throw new ConfigurationException("The path to a certificate needs to be provided when using https. Please use the argument 'certificatefilepath' to provide it.");

            var defaultApiHost = this.UseHttps
                ? DefaultApiHost.Replace(@"http://", @"https://")
                : DefaultApiHost;

            string apiHost = config.GetOrDefault("unityapi_apiuri", defaultApiHost, this.logger);
            var apiUri = new Uri(apiHost);

            // Find out which port should be used for the API.
            int apiPort = config.GetOrDefault("unityapi_apiport", DefaultAPIPort, this.logger);

            // If no port is set in the API URI.
            if (apiUri.IsDefaultPort)
            {
                this.ApiUri = new Uri($"{apiHost}:{apiPort}");
                this.ApiPort = apiPort;
            }
            // If a port is set in the -apiuri, it takes precedence over the default port or the port passed in -apiport.
            else
            {
                this.ApiUri = apiUri;
                this.ApiPort = apiUri.Port;
            }

            // Set the keepalive interval (set in seconds).
            int keepAlive = config.GetOrDefault("unityapi_keepalive", 0, this.logger);
            if (keepAlive > 0)
            {
                this.KeepaliveTimer = new Timer
                {
                    AutoReset = false,
                    Interval = keepAlive * 1000
                };
            }
        }

        /// <summary>Prints the help information on how to configure the API settings to the logger.</summary>
        /// <param name="network">The network to use.</param>
        public static void PrintHelp(Network network)
        {
            var builder = new StringBuilder();
            
            builder.AppendLine($"-unityapi_enable=<bool>                    Use unity3d API. Defaults to false.");
            builder.AppendLine($"-unityapi_apiuri=<string>                  URI to node's API interface. Defaults to '{ DefaultApiHost }'.");
            builder.AppendLine($"-unityapi_apiport=<0-65535>                Port of node's API interface. Defaults to { network.DefaultAPIPort }.");
            builder.AppendLine($"-unityapi_keepalive=<seconds>              Keep Alive interval (set in seconds). Default: 0 (no keep alive).");
            builder.AppendLine($"-unityapi_usehttps=<bool>                  Use https protocol on the API. Defaults to false.");
            builder.AppendLine($"-unityapi_certificatefilepath=<string>     Path to the certificate used for https traffic encryption. Defaults to <null>. Password protected files are not supported. On MacOs, only p12 certificates can be used without password.");

            NodeSettings.Default(network).Logger.LogInformation(builder.ToString());
        }

        /// <summary>
        /// Get the default configuration.
        /// </summary>
        /// <param name="builder">The string builder to add the settings to.</param>
        /// <param name="network">The network to base the defaults off.</param>
        public static void BuildDefaultConfigurationFile(StringBuilder builder, Network network)
        {
            builder.AppendLine("####Unity3d API Settings####");
            builder.AppendLine($"#Enable unity3d api support");
            builder.AppendLine($"#unityapi_enable=");
            builder.AppendLine($"#URI to node's API interface. Defaults to '{ DefaultApiHost }'.");
            builder.AppendLine($"#unityapi_apiuri={ DefaultApiHost }");
            builder.AppendLine($"#Port of node's API interface. Defaults to { network.DefaultAPIPort }.");
            builder.AppendLine($"#unityapi_apiport={ network.DefaultAPIPort }");
            builder.AppendLine($"#Keep Alive interval (set in seconds). Default: 0 (no keep alive).");
            builder.AppendLine($"#unityapi_keepalive=0");
            builder.AppendLine($"#Use HTTPS protocol on the API. Default is false.");
            builder.AppendLine($"#unityapi_usehttps=false");
            builder.AppendLine($"#Path to the file containing the certificate to use for https traffic encryption. Password protected files are not supported. On MacOs, only p12 certificates can be used without password.");
            builder.AppendLine(@"#Please refer to .Net Core documentation for usage: 'https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate2.-ctor?view=netcore-2.1#System_Security_Cryptography_X509Certificates_X509Certificate2__ctor_System_Byte___'.");
            builder.AppendLine($"#unityapi_certificatefilepath=");
        }
    }
}
