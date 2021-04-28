namespace Stratis.Features.SystemContracts
{
    public class SystemContractCall
    {
        public SystemContractCall(string methodName, string type, object[] parameters, int version = 1)
        {
            this.MethodName = methodName;
            this.Type = type;
            this.Parameters = parameters;
            this.Version = version;
        }

        public string MethodName { get; }
        public string Type { get; }
        public object[] Parameters { get; }
        public int Version { get; }
    }
}
