using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Reflection.PortableExecutable;
using CSharpFunctionalExtensions;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;

namespace Stratis.SmartContracts.CLR.Decompilation
{
    public class CSharpContractDecompiler
    {
        public Result<string> GetSource(byte[] bytecode)
        {
            if (bytecode == null)
                return Result.Fail<string>("Bytecode cannot be null");

            using (var memStream = new MemoryStream(bytecode))
            {
                try
                {
                    var peFile = new PEFile("placeholder", memStream);
                    var resolver = new UniversalAssemblyResolver(null, false, null, null, PEStreamOptions.Default);
                    var folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    resolver.AddSearchDirectory(folder);
                    var decompiler = new CSharpDecompiler(peFile, resolver, new DecompilerSettings());
                    string cSharp = decompiler.DecompileWholeModuleAsString();
                    return Result.Ok(cSharp);
                }
                catch (BadImageFormatException e)
                {
                    return Result.Fail<string>(e.Message);
                }
            }
        }
    }
}
