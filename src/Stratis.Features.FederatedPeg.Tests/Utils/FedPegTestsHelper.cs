using System.Linq;
using NBitcoin;
using NBitcoin.Protocol;
using Stratis.Bitcoin.Configuration;

namespace Stratis.Features.FederatedPeg.Tests.Utils
{
    public static class FedPegTestsHelper
    {
        public static FederatedPegSettings CreateSettings(Network network, out NodeSettings nodeSettings)
        {
            FederationId federationId = network.Federations.GetOnlyFederation().Id;
            string redeemScript = PayToFederationTemplate.Instance.GenerateScriptPubKey(federationId).ToString();
            string federationIps = "127.0.0.1:36201,127.0.0.1:36202,127.0.0.1:36203";
            string multisigPubKey = network.Federations.GetFederation(federationId).GetFederationDetails().pubKeys.TakeLast(1).First().ToHex();
            string[] args = new[] { "-sidechain", "-regtest", $"-federationips={federationIps}", $"-redeemscript={redeemScript}", $"-publickey={multisigPubKey}", "-mincoinmaturity=1", "-mindepositconfirmations=1" };
            nodeSettings = new NodeSettings(network, ProtocolVersion.ALT_PROTOCOL_VERSION, args: args);

            var settings = new FederatedPegSettings(nodeSettings);

            return settings;
        }
    }
}
