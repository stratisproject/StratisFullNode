using System.Linq;
using System.Runtime.CompilerServices;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.IntegrationTests.Common;
using Stratis.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Tests.Common;
using Stratis.SmartContracts.Networks;

namespace Stratis.SmartContracts.Tests.Common
{
    public class SmartContractNodeBuilder : NodeBuilder
    {
        public EditableTimeProvider TimeProvider { get; }

        public SmartContractNodeBuilder(string rootFolder) : base(rootFolder)
        {
            this.TimeProvider = new EditableTimeProvider();
        }

        public CoreNode CreateSmartContractPoANode(SmartContractsPoARegTest network, int nodeIndex)
        {
            string dataFolder = this.GetNextDataFolderName();

            CoreNode node = this.CreateNode(new SmartContractPoARunner(dataFolder, network, this.TimeProvider), "poa.conf");

            var settings = new NodeSettings(network, args: new string[] { "-conf=poa.conf", "-datadir=" + dataFolder });

            var tool = new KeyTool(settings.DataFolder);
            tool.SavePrivateKey(network.FederationKeys[nodeIndex]);

            return node;
        }

        public CoreNode CreateStraxNode(StraxRegTest network, int nodeIndex)
        {
            string dataFolder = this.GetNextDataFolderName();

            CoreNode node = this.CreateNode(new StraxRunner(dataFolder, network, this.TimeProvider), "strax.conf");

            var settings = new NodeSettings(network, args: new string[] { "-conf=strax.conf", "-datadir=" + dataFolder });

            var federationMnemonics = new[] {
                "ensure feel swift crucial bridge charge cloud tell hobby twenty people mandate",
                "quiz sunset vote alley draw turkey hill scrap lumber game differ fiction",
                "exchange rent bronze pole post hurry oppose drama eternal voice client state"
               }.Select(m => new Mnemonic(m, Wordlist.English)).ToList();

            var federationKeys = federationMnemonics.Select(m => m.DeriveExtKey().PrivateKey).ToList();

            var tool = new KeyTool(settings.DataFolder);
            tool.SavePrivateKey(federationKeys[nodeIndex]);

            return node;
        }

        public CoreNode CreateWhitelistedContractPoANode(SmartContractsPoAWhitelistRegTest network, int nodeIndex)
        {
            string dataFolder = this.GetNextDataFolderName();

            CoreNode node = this.CreateNode(new WhitelistedContractPoARunner(dataFolder, network, this.TimeProvider), "poa.conf");
            var settings = new NodeSettings(network, args: new string[] { "-conf=poa.conf", "-datadir=" + dataFolder });

            var tool = new KeyTool(settings.DataFolder);
            tool.SavePrivateKey(network.FederationKeys[nodeIndex]);
            return node;
        }

        public CoreNode CreateSmartContractPowNode()
        {
            Network network = new SmartContractsRegTest();
            return CreateNode(new StratisSmartContractNode(this.GetNextDataFolderName(), network), "stratis.conf");
        }

        public static SmartContractNodeBuilder Create(object caller, [CallerMemberName] string callingMethod = null)
        {
            string testFolderPath = TestBase.CreateTestDir(caller, callingMethod);
            var builder = new SmartContractNodeBuilder(testFolderPath);
            builder.WithLogsDisabled();
            return builder;
        }
    }
}
