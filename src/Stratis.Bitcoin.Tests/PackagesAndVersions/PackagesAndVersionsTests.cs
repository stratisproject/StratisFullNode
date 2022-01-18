using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;
using Microsoft.Build.Construction;
using Microsoft.Extensions.DependencyInjection;
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

            var projectsByPath = solutionFile.ProjectsByGuid
                .Where(p => p.Value.ProjectName.StartsWith("Stratis.") || extra.Contains(p.Value.ProjectName))
                .ToDictionary(p => p.Value.AbsolutePath, p => p.Value);
            var projectFiles = projectsByPath.ToDictionary(p => p.Key, p => { XmlDocument doc = new XmlDocument(); doc.Load(p.Key); return doc; });
            var referencedVersions = projectFiles.ToDictionary(p => p.Key, p => p.Value.SelectSingleNode("Project/PropertyGroup/Version")?.InnerText);
            var projectsToCheck = new List<string>(projectsByPath.Keys);

            var debugLog = new StringBuilder();

            while (projectsToCheck.Count > 0)
            {
                string projectFolder = projectsToCheck.First();
                projectsToCheck.RemoveAt(0);

                ProjectInSolution project = projectsByPath[projectFolder];

                // Read project file.
                XmlDocument doc = projectFiles[projectFolder];

                string name = doc.SelectSingleNode("Project/PropertyGroup/PackageId")?.InnerText;
                if (name == null)
                    continue;

                string version = doc.SelectSingleNode("Project/PropertyGroup/Version")?.InnerText;
                if (version == null)
                    continue;

                if (version.EndsWith(".0"))
                    version = version.Substring(0, version.Length - 2);

                // Check the referenced projects first.
                var references = new List<string>();
                foreach (XmlNode x in doc.SelectNodes("/Project/ItemGroup/ProjectReference"))
                {
                    string includePath = x.Attributes["Include"].Value;
                    string referencedProject = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFolder), includePath));

                    if (projectsToCheck.Contains(referencedProject))
                    {
                        references.Add(referencedProject);
                        projectsToCheck.Remove(referencedProject);
                    }
                }

                if (references.Count > 0)
                {
                    projectsToCheck.InsertRange(0, references);
                    projectsToCheck.Add(projectFolder);
                    continue;
                }

                string targetName = $"{name.ToLower()}.{version}-nuget.symbols";
                string targetFolder = Path.Combine(Path.GetDirectoryName(projectFolder), "bin", "Release");
                string targetPath = Path.Combine(targetFolder, targetName);
                if (!Directory.Exists(targetPath))
                {
                    if (!Directory.Exists(targetFolder))
                        Directory.CreateDirectory(targetFolder);

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
                if (Directory.Exists(packageSource) && DirectoryEquals(packageSource, Path.GetDirectoryName(projectFolder), debugLog))
                {
                    // Even though the project file may be unchanged the references could be referring to different versions.

                    // Load the NUSPEC file from the package.
                    string nuspecFile = Path.Combine(targetPath, $"{name}.nuspec");
                    XmlDocument doc2 = new XmlDocument();
                    doc2.Load(nuspecFile);

                    bool referencedPackagesMatch = true;
                    foreach (XmlNode x in doc.SelectNodes("/Project/ItemGroup/ProjectReference"))
                    {
                        string includePath = x.Attributes["Include"].Value;
                        string includeFullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFolder), includePath));

                        XmlDocument doc3 = projectFiles[includeFullPath];
                        string name3 = doc3.SelectSingleNode("Project/PropertyGroup/PackageId")?.InnerText;
                        if (name3 == null)
                            continue;

                        XmlNode depNode = doc2.SelectSingleNode($"//*[name()='dependency' and @id='{name3}']");
                        string cmpVersion = depNode.Attributes["version"].Value;

                        if (cmpVersion != referencedVersions[includeFullPath])
                        {
                            string msg = $"Comparing the local project '{project.ProjectName}' version {version} with its published package, '{targetName}', the published package references version '{cmpVersion}' of '{name3}' while the local project references version '{referencedVersions[includeFullPath]}'.";
                            debugLog.AppendLine(msg);
                            referencedPackagesMatch = false;
                            break;
                        }
                    }

                    if (referencedPackagesMatch)
                        continue;
                }

                string msg2 = $"The local project '{project.ProjectName}' differs from the published package but its version {version} is the same.";
                debugLog.AppendLine(msg2);

                modifiedPackages.Add(project.ProjectName);
                referencedVersions[projectFolder] += " (modified)";
            }

            Assert.True(modifiedPackages.Count == 0, $"{debugLog.ToString()} Affected packages: {string.Join(", ", modifiedPackages)}");
        }

        static bool DirectoryEquals(string directory1, string directory2, StringBuilder debugLog)
        {
            foreach (string fileName1 in Directory.EnumerateFiles(directory1))
            {
                string fileName2 = Path.Combine(directory2, Path.GetFileName(fileName1));
                if (!FileEquals(fileName1, fileName2, debugLog))
                    return false;
            }

            foreach (string subFolder in Directory.EnumerateDirectories(directory1))
            {
                string directoryName = Path.GetFileName(subFolder);
                if (!DirectoryEquals(subFolder, Path.Combine(directory2, directoryName), debugLog))
                    return false;
            }

            return true;
        }

        static bool FileEquals(string fileName1, string fileName2, StringBuilder debugLog)
        {
            try
            {
                IEnumerable<string> source = File.ReadLines(fileName2);

                foreach (string line in File.ReadLines(fileName1))
                {
                    string compare = source.Take(1).FirstOrDefault();

                    if (compare?.Trim() != line.Trim())
                    {
                        debugLog.AppendLine($"'{fileName1}' differs from '{fileName2}' on these lines:{Environment.NewLine}'{line}', and {Environment.NewLine}'{compare}' respectively.");
                        return false;
                    }

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
