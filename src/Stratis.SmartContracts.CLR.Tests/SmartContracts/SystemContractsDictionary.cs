using Stratis.SmartContracts;

public struct WhiteListEntry
{
    public UInt256 CodeHash;
    public Address LastAddress;
    public string Name;
}

public class SystemContractsDictionary : SmartContract
{
    const string primaryGroup = "main";

    public SystemContractsDictionary(ISmartContractState state) : base(state)
    {
        this.State.SetArray($"Signatories:{primaryGroup}", new[] { new Address(0, 0, 0, 0, 0), new Address(0, 0, 0, 0, 1), new Address(0, 0, 0, 0, 2) });
        this.State.SetUInt32($"Quorum:{primaryGroup}", 2);
    }

    public Address[] Signatories => GetSignatories(primaryGroup);

    public uint Quorum => GetQuorum(primaryGroup);

    private void VerifySignatures(string authorizationChallenge)
    {
        /*
        Assert(this.VerifySignatures(System.Text.Encoding.ASCII.GetBytes(authorizationChallenge), signatures, this.Signatories >= this.Quorum,
            $"Please provide {this.Quorum} valid signatures for '{authorizationChallenge}'.");
        */
    }

    public Address[] GetSignatories(string group)
    {
        return this.State.GetArray<Address>($"Signatories:{group}");
    }

    public uint GetQuorum(string group)
    {
        return this.State.GetUInt32($"Quorum:{group}");
    }

    public void SetQuorum(string group, uint quorum)
    {
        uint oldQuorum = this.State.GetUInt32($"Quorum:{group}");
        uint nonce = this.State.GetUInt32($"GroupNonce:{group}");

        Assert(quorum != oldQuorum, "Nothing changed.");
        Assert(quorum <= GetSignatories(group).Length, "The quorum can't exceed the number of signatories.");

        this.VerifySignatures((oldQuorum == default(uint)) ?
            $"{nameof(SetQuorum)}(Nonce:{nonce},Group:{group},Quorum:{quorum})" :
            $"{nameof(SetQuorum)}(Nonce:{nonce},Group:{group},Quorum:{oldQuorum}=>{quorum})");

        this.State.SetUInt32($"Quorum:{group}", quorum);
        this.State.SetUInt32($"GroupNonce:{group}", nonce + 1);
    }

    public void AddSignatory(string group, Address address)
    {
        Address[] signatories = this.GetSignatories(group);
        for (int i = 0; i < signatories.Length; i++)
            Assert(signatories[i] != address, "The signatory already exists.");

        uint nonce = this.State.GetUInt32($"GroupNonce:{group}");

        this.VerifySignatures($"{nameof(AddSignatory)}(Nonce:{nonce},Group:{group},Address:{address})");

        System.Array.Resize(ref signatories, signatories.Length + 1);
        signatories[signatories.Length - 1] = address;

        this.State.SetArray($"Signatories:{group}", signatories);
        this.State.SetUInt32($"GroupNonce:{group}", nonce + 1);
    }

    public void RemoveSignatory(string group, Address address)
    {
        bool found = false;
        Address[] signatories = this.GetSignatories(group);
        for (int i = 0; i < signatories.Length; i++)
        {
            if (signatories[i] == address)
            {
                found = true;
                for (int j = i + 1; j < signatories.Length; j++)
                    signatories[j - 1] = signatories[j];

                System.Array.Resize(ref signatories, signatories.Length - 1);
            }
        }

        Assert(found, "The signatory does not exist.");
        Assert(signatories.Length >= GetQuorum(group), "The number of signatories can't be less than the quorum.");

        uint nonce = this.State.GetUInt32($"GroupNonce:{group}");

        this.VerifySignatures($"{nameof(RemoveSignatory)}(Nonce:{nonce},Group:{group},Address:{address})");

        this.State.SetArray($"Signatories:{group}", signatories);
        this.State.SetUInt32($"GroupNonce:{group}", nonce + 1);
    }

    public bool IsWhiteListed(UInt256 codeHash)
    {
        Assert(codeHash != default(UInt256));

        WhiteListEntry whiteListEntry = this.State.GetStruct<WhiteListEntry>(codeHash.ToString());

        return whiteListEntry.CodeHash != default(UInt256);
    }

    public UInt256 GetCodeHash(string name)
    {
        Assert(!string.IsNullOrEmpty(name));

        return this.State.GetUInt256($"ByName:{name}");
    }

    public Address GetContractAddress(string name)
    {
        Assert(!string.IsNullOrEmpty(name));

        UInt256 codeHash = this.State.GetUInt256($"ByName:{name}");

        if (codeHash == default(UInt256))
            return default(Address);

        WhiteListEntry whiteListEntry = this.State.GetStruct<WhiteListEntry>(codeHash.ToString());

        return whiteListEntry.LastAddress;
    }

    public Address GetContractAddress(UInt256 codeHash)
    {
        Assert(codeHash != default(UInt256));

        WhiteListEntry whiteListEntry = this.State.GetStruct<WhiteListEntry>(codeHash.ToString());

        return whiteListEntry.LastAddress;
    }

    public void WhiteList(byte[] signatures, UInt256 codeHash, Address lastAddress, string name)
    {
        Assert(signatures != null);
        Assert(codeHash != default(UInt256));

        UInt256 codeHashKey = codeHash;
        WhiteListEntry whiteListEntry;

        if (!string.IsNullOrEmpty(name))
        {
            codeHashKey = this.State.GetUInt256($"ByName:{name}");

            if (codeHashKey == default(UInt256))
                codeHashKey = codeHash;
        }

        whiteListEntry = this.State.GetStruct<WhiteListEntry>(codeHashKey.ToString());

        uint nonce = this.State.GetUInt32($"Nonce:{codeHash}");

        string authorizationChallenge;
        if (whiteListEntry.CodeHash == default(UInt256))
        {
            authorizationChallenge = $"{nameof(WhiteList)}(Nonce:{nonce},CodeHash:{codeHash},LastAddress:{lastAddress},Name:{name})";
        }
        else
        {
            Assert(whiteListEntry.CodeHash != codeHash || whiteListEntry.LastAddress != lastAddress || whiteListEntry.Name != name, "Nothing changed.");
            authorizationChallenge = $"{nameof(WhiteList)}(Nonce:{nonce},CodeHash:{whiteListEntry.CodeHash}=>{codeHash},LastAddress:{whiteListEntry.LastAddress}=>{lastAddress},Name:{whiteListEntry.Name}=>{name})";
        }

        this.VerifySignatures(authorizationChallenge);

        if (whiteListEntry.CodeHash != default(UInt256))
        {
            if (codeHash != whiteListEntry.CodeHash)
                this.State.Clear(whiteListEntry.CodeHash.ToString());

            if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(whiteListEntry.Name))
                this.State.Clear($"ByName:{whiteListEntry.Name}");
        }

        whiteListEntry.CodeHash = codeHash;
        whiteListEntry.LastAddress = lastAddress;
        whiteListEntry.Name = name;

        this.State.SetStruct<WhiteListEntry>(codeHash.ToString(), whiteListEntry);
        this.State.SetUInt256($"ByName:{name}", codeHash);
        this.State.SetUInt32($"Nonce:{codeHash}", nonce + 1);
    }

    public void BlackList(byte[] signatures, UInt256 codeHash)
    {
        Assert(signatures != null);
        Assert(codeHash != default(UInt256));

        WhiteListEntry whiteListEntry = this.State.GetStruct<WhiteListEntry>(codeHash.ToString());

        Assert(whiteListEntry.CodeHash != default(UInt256), "The entry does not exist.");

        uint nonce = this.State.GetUInt32($"Nonce:{codeHash}");

        this.VerifySignatures($"{nameof(BlackList)}(Nonce:{nonce},CodeHash:{whiteListEntry.CodeHash},LastAddress:{whiteListEntry.LastAddress},Name:{whiteListEntry.Name})");

        this.State.Clear(codeHash.ToString());
        this.State.Clear($"ByName:{whiteListEntry.Name}");
        this.State.SetUInt32($"Nonce:{codeHash}", nonce + 1);
    }
}