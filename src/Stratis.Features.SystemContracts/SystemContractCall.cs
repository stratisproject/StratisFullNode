using NBitcoin;

namespace Stratis.Features.SystemContracts
{
    public class SystemContractCall
    {
        public SystemContractCall(uint160 idenfitier, string methodName, string type, object[] parameters, int version = 1)
        {
            this.Identifier = idenfitier;
            this.MethodName = methodName;
            this.Type = type;
            this.Parameters = parameters;
            this.Version = version;
        }

        public uint160 Identifier { get; }
        public string MethodName { get; }
        public string Type { get; }
        public object[] Parameters { get; }
        public int Version { get; }
    }
}
