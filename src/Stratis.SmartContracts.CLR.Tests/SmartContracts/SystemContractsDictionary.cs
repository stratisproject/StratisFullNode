namespace Stratis.SmartContracts.CLR.Tests.SmartContracts
{
    public struct WhiteListEntry
    {
        public UInt256 CodeHash;
        public string LastAddress;
        public string Name;
    }

    public class SystemContractsDictionary : SmartContract
    {
        public SystemContractsDictionary(ISmartContractState state) : base(state)
        {
        }

        public void WhiteList(string[] signatures, UInt256 codeHash, string lastAddress, string name)
        {
            Assert(signatures != null && signatures.Length > 0);
            Assert(codeHash != default);

            UInt256 codeHashKey = default;
            WhiteListEntry whiteListEntry;
            if (!string.IsNullOrEmpty(name))
            {
                codeHashKey = this.State.GetUInt256($"ByName:{name}");
                if (codeHashKey == default)
                {
                    whiteListEntry = this.State.GetStruct<WhiteListEntry>(codeHash.ToString());
                    Assert(whiteListEntry.CodeHash == default);
                }
                else
                {
                    whiteListEntry = this.State.GetStruct<WhiteListEntry>(codeHashKey.ToString());
                }
            }
            else
            {
                whiteListEntry = this.State.GetStruct<WhiteListEntry>(codeHash.ToString());
            }

            string message = "WhiteList(";
            message += $"CodeHash:{whiteListEntry.CodeHash}=>{codeHash},";
            message += $"LastAddress:{whiteListEntry.LastAddress}=>{lastAddress},";
            message += $"Name:{whiteListEntry.Name}=>{name})";

            this.VerifySignatures(message, signatures);

            whiteListEntry.CodeHash = codeHash;
            whiteListEntry.LastAddress = lastAddress;
            whiteListEntry.Name = name;

            if (codeHashKey != default)
                this.State.Clear(codeHashKey.ToString());
            this.State.SetStruct<WhiteListEntry>(codeHash.ToString(), whiteListEntry);
            this.State.SetUInt256($"ByName:{name}", codeHash);
        }

        public void BlackList(string[] signatures, UInt256 codeHash)
        {
            UInt256 codeHashKey = codeHash;

            WhiteListEntry whiteListEntry = this.State.GetStruct<WhiteListEntry>(codeHash.ToString());

            string message = "BlackList(";
            message += $"CodeHash:{whiteListEntry.CodeHash},";
            message += $"LastAddress:{whiteListEntry.LastAddress},";
            message += $"Name:{whiteListEntry.Name})";

            this.VerifySignatures(message, signatures);

            this.State.Clear(codeHashKey.ToString());
            this.State.Clear($"ByName:{whiteListEntry.Name}");
        }
    }
}
