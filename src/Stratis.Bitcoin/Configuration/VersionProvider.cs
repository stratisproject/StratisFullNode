using System.Reflection;
using System.Text.RegularExpressions;
using Stratis.Bitcoin.Interfaces;

namespace Stratis.Bitcoin.Configuration
{
    public class VersionProvider : IVersionProvider
    {
        public string GetVersion()
        {
            return GetVersion(true);
        }

        public string GetVersionNoSuffix()
        {
            return GetVersion(false);
        }

        private static string GetVersion(bool includeSuffix)
        {
            var versionStr = typeof(VersionProvider).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
            Match match = Regex.Match(versionStr, "([0-9]+)\\.([0-9]+)\\.([0-9]+)\\.([0-9]+)(.*)");
            string major = match.Groups[1].Value;
            string minor = match.Groups[2].Value;
            string build = match.Groups[3].Value;
            string revision = match.Groups[4].Value;
            string suffix = includeSuffix ? match.Groups[5].Value : string.Empty;
            return $"{major}.{minor}.{build}.{revision}{suffix}";
        }
    }
}