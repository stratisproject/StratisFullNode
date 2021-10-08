using System.IO;
using System.Linq;
using Stratis.Bitcoin.Configuration;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    public sealed class ReconstructFederationService
    {
        private readonly NodeSettings nodeSettings;


        public ReconstructFederationService(NodeSettings nodeSettings)
        {
            this.nodeSettings = nodeSettings;
        }

        public void SetReconstructionFlag(bool reconstructOnStartup)
        {
            string[] configLines = File.ReadAllLines(this.nodeSettings.ConfigurationFile);

            if (configLines.Any(c => c.Contains(PoAFeature.ReconstructFederationFlag)))
            {
                for (int i = 0; i < configLines.Length; i++)
                {
                    if (configLines[i].Contains(PoAFeature.ReconstructFederationFlag))
                        configLines[i] = $"{PoAFeature.ReconstructFederationFlag}={reconstructOnStartup}";
                }

                File.WriteAllLines(this.nodeSettings.ConfigurationFile, configLines);
            }
            else
            {
                using (StreamWriter sw = File.AppendText(this.nodeSettings.ConfigurationFile))
                {
                    sw.WriteLine($"{PoAFeature.ReconstructFederationFlag}={reconstructOnStartup}");
                };
            }
        }
    }
}
