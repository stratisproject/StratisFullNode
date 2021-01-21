﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.Loader;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;

namespace Stratis.SmartContracts.CLR.Loader
{
    public class SmartContractLoadContext : AssemblyLoadContext
    {
        private readonly ILogger logger;
        private readonly AssemblyLoadContext defaultContext;
        private static Dictionary<string, byte[]> cache = new Dictionary<string, byte[]>();

        public SmartContractLoadContext(AssemblyLoadContext defaultContext, ILogger logger)
        {
            this.logger = logger;
            this.defaultContext = defaultContext;
        }

        public static string GetExactAssembly(AssemblyName assemblyName, out string errorMessage)
        {
            // The DLL is not included with our distribution. See if we can get it from NuGet.org.
            string version = assemblyName.Version.ToString();
            string folderName = $"{assemblyName.Name.ToLower()}.{version}";
            string assemblyFolder = Path.GetFullPath("LegacyStandardsDLLs");
            string assemblyPath = Path.Combine(assemblyFolder, folderName);

            errorMessage = null;

            if (!Directory.Exists(assemblyPath))
            {
                string downloadFile = $"{assemblyPath}.nupkg";

                if (!File.Exists(downloadFile))
                {
                    Directory.CreateDirectory(assemblyFolder);

                    string downloadLink = $"https://www.nuget.org/api/v2/package/{assemblyName.Name.ToLower()}/{version}";

                    try
                    {
                        using (var client = new WebClient())
                        {
                            client.DownloadFile(downloadLink, downloadFile);
                        }
                    }
                    catch (Exception)
                    {
                        errorMessage = $"Could not find '{downloadFile}'. Get the file from '{downloadLink}' and copy it to this location.";
                        return null;
                    }
                }

                ZipFile.ExtractToDirectory(downloadFile, assemblyPath);
            }

            string[] files = Directory.GetFiles(Path.Combine(assemblyPath, "lib"), $"{assemblyName.Name}.dll", SearchOption.AllDirectories);
            return  (files.Length == 1) ? files[0] : null;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            // Ensure that an exact compatible version is used.
            if (assemblyName.Name == "Stratis.SmartContracts" || assemblyName.Name == "Stratis.SmartContracts.Standards")
            {
                if (!cache.TryGetValue(assemblyName.FullName, out byte[] bytes))
                {
                    string exactAssembly = GetExactAssembly(assemblyName, out string error);
                    if (exactAssembly == null)
                    {
                        if (this.logger == null)
                            throw new Exception(error);

                        cache[assemblyName.FullName] = null;
                        this.logger.LogWarning(error);
                        return this.defaultContext.LoadFromAssemblyName(assemblyName);
                    }

                    bytes = File.ReadAllBytes(exactAssembly);
                    cache[assemblyName.FullName] = bytes;
                }

                if (bytes == null)
                    return this.defaultContext.LoadFromAssemblyName(assemblyName);

                using (var stream = new MemoryStream(bytes))
                {
                    return this.LoadFromStream(stream);
                }
            }

            return this.defaultContext.LoadFromAssemblyName(assemblyName);
        }
    }

    /// <summary>
    /// Loads assemblies from bytecode.
    /// </summary>
    public class ContractAssemblyLoader : ILoader
    {
        private readonly ILogger logger;

        public ContractAssemblyLoader(ILoggerFactory loggerFactory = null)
        {
            this.logger = loggerFactory?.CreateLogger(typeof(ContractAssemblyLoader).Name);
        }

        /// <summary>
        /// Loads a contract from a raw byte array into a custom <see cref="AssemblyLoadContext"/>.
        /// </summary>
        public Result<IContractAssembly> Load(ContractByteCode bytes)
        {
            // Assembly.Load(byte[]) loads the assembly into a custom AssemblyLoadContext
            try
            {
                SmartContractLoadContext context = new SmartContractLoadContext(AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly()), this.logger) ;

                MemoryStream s = new MemoryStream(bytes.Value);
                Assembly assembly = context.LoadFromStream(s);

                return Result.Ok<IContractAssembly>(new ContractAssembly(assembly));
            }
            catch (BadImageFormatException e)
            {
                return Result.Fail<IContractAssembly>(e.Message);
            }
        }
    }
}