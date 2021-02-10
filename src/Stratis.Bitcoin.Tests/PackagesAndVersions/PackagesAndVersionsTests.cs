using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Xml;
using Microsoft.Build.Construction;
using Xunit;

namespace Stratis.Bitcoin.Tests.PackagesAndVersions
{
    public class PackagesAndVersionsTests
    {
        [Fact]
        public void EnsureVersionsBumpedWhenChangingPublishedPackages()
        {
            // 1) Read solution file and get all projects with package information.
            // 2) Retrieve the package from NuGet if not retrieved already
            // 3) If the source code is different and same version then this is an error.

            var modifiedPackages = new List<string>();

            string sourceFolder = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 5; i++)
                sourceFolder = Directory.GetParent(sourceFolder).ToString();

            string solutionFilePath = Directory.EnumerateFiles(sourceFolder, "*.sln").First();
            SolutionFile solutionFile = SolutionFile.Parse(solutionFilePath);
            HashSet<string> extra = new HashSet<string>() { "NBitcoin", "FodyNlogAdapter" };

            foreach (ProjectInSolution project in solutionFile.ProjectsByGuid.Values)
            {
                if (!project.ProjectName.StartsWith("Stratis.") && !extra.Contains(project.ProjectName))
                    continue;

                // Read project file.
                XmlDocument doc = new XmlDocument();
                doc.Load(project.AbsolutePath);
                string name = doc.SelectSingleNode("Project/PropertyGroup/PackageId")?.InnerText;
                if (name == null)
                    continue;

                string version = doc.SelectSingleNode("Project/PropertyGroup/Version")?.InnerText;
                if (version == null)
                    continue;

                if (version.EndsWith(".0"))
                    version = version.Substring(0, version.Length - 2);

                string projectFolder = Path.GetDirectoryName(project.AbsolutePath);
                string targetName = $"{name.ToLower()}.{version}-nuget.symbols";
                string targetFolder = Path.Combine(projectFolder, $"bin{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}");
                string targetPath = Path.Combine(targetFolder, targetName);
                if (!Directory.Exists(targetPath))
                {
                    string targetFile = Path.Combine(projectFolder, $"{targetPath}.nupkg");
                    if (!File.Exists(targetFile))
                    {
                        string url = $"https://www.nuget.org/api/v2/package/{name}/{version}";
                        // Only if its on NuGet too.
                        using (var client = new WebClient())
                        {
                            try
                            {
                                client.DownloadFile(url, targetFile);
                            }
                            catch (Exception)
                            {
                                if (File.Exists(targetFile))
                                    File.Delete(targetFile);

                                continue;
                            }
                        }
                    }

                    ZipFile.ExtractToDirectory(targetFile, targetPath);
                }

                // Compare source with files from NuGet.
                string packageSource = Path.Combine(targetPath, "src", project.ProjectName);
                if (Directory.Exists(packageSource) && DirectoryEquals(packageSource, projectFolder))
                    continue;

                modifiedPackages.Add(project.ProjectName);
            }

            Assert.Empty(modifiedPackages);
        }

        static bool DirectoryEquals(string directory1, string directory2)
        {
            foreach (string fileName1 in Directory.EnumerateFiles(directory1))
            {
                string fileName2 = Path.Combine(directory2, Path.GetFileName(fileName1));
                if (!FileEquals(fileName1, fileName2))
                    return false;
            }

            foreach (string subFolder in Directory.EnumerateDirectories(directory1))
            {
                string directoryName = Path.GetFileName(subFolder);
                if (!DirectoryEquals(subFolder, Path.Combine(directory2, directoryName)))
                    return false;
            }

            return true;
        }

        static bool FileEquals(string fileName1, string fileName2)
        {
            try
            {
                IEnumerable<string> source = File.ReadLines(fileName2);

                foreach (string line in File.ReadLines(fileName1))
                {
                    if (source.Take(1).FirstOrDefault() != line)
                        return false;

                    source = source.Skip(1);
                    continue;
                }

                return source.Take(1).FirstOrDefault() == null;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
