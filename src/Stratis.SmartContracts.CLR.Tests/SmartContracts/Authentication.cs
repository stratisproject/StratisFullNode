using System.Linq;
using NBitcoin;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR;
using ECRecover = Stratis.SCL.Crypto.ECRecover;

public class Authentication : SmartContract
{
    const string primaryGroup = "main";
    private readonly uint version;

    public Authentication(ISmartContractState state, Network network) : base(state)
    {
        uint version = new EmbeddedContractIdentifier(state.Message.ContractAddress.ToUint160()).Version;

        Assert(version == 1, "Only a version of 1 is supported.");

        this.version = version;

        // Exit if already initialized.
        if (this.Initialized)
            return;

        PrimaryAuthenticators primaryAuthenticators = network.EmbeddedContractContainer.PrimaryAuthenticators;

        Assert(primaryAuthenticators != null && primaryAuthenticators.Signatories.Length >= primaryAuthenticators.Quorum && primaryAuthenticators.Quorum >= 1);

        this.SetSignatories(primaryGroup, primaryAuthenticators.Signatories.Select(k => ((BitcoinPubKeyAddress)BitcoinAddress.Create(k, network)).Hash.ToBytes().ToAddress()).ToArray());
        this.SetQuorum(primaryGroup, primaryAuthenticators.Quorum);

        this.Initialized = true;
    }

    public bool Initialized 
    {
        get => this.State.GetBool("Initialized");
        private set => this.State.SetBool("Initialized", value);
    }

    public void VerifySignatures(string group, byte[] signatures, string authorizationChallenge)
    {
        string[] sigs = this.Serializer.ToArray<string>(signatures);

        Assert(ECRecover.TryGetVerifiedSignatures(sigs, authorizationChallenge, this.GetSignatories(group), out Address[] verifieds), "Invalid signatures");

        uint quorum = this.GetQuorum(group);

        Assert(verifieds.Length >= quorum, $"Please provide {quorum} valid signatures for '{authorizationChallenge}' from '{group}'.");
    }

    public Address[] GetSignatories(string group)
    {
        Assert(!string.IsNullOrEmpty(group));
        return this.State.GetArray<Address>($"Signatories:{group}");
    }

    private void SetSignatories(string group, Address[] values)
    {
        this.State.SetArray($"Signatories:{group}", values);
    }

    public uint GetQuorum(string group)
    {
        Assert(!string.IsNullOrEmpty(group));
        return this.State.GetUInt32($"Quorum:{group}");
    }

    private void SetQuorum(string group, uint value)
    {
        this.State.SetUInt32($"Quorum:{group}", value);
    }

    private uint GetGroupNonce(string group)
    {
        return this.State.GetUInt32($"GroupNonce:{group}");
    }

    private void SetGroupNonce(string group, uint value)
    {
        this.State.SetUInt32($"GroupNonce:{group}", value);
    }

    public void AddSignatory(byte[] signatures, string group, Address address, uint newSize, uint newQuorum)
    {
        Assert(!string.IsNullOrEmpty(group));
        Assert(newSize >= newQuorum, "The number of signatories can't be less than the quorum.");

        Address[] signatories = this.GetSignatories(group);
        foreach (Address signatory in signatories)
            Assert(signatory != address, "The signatory already exists.");

        Assert((signatories.Length + 1) == newSize, "The expected size is incorrect.");

        // The nonce is used to prevent replay attacks.
        uint nonce = this.GetGroupNonce(group);

        // Validate or provide a unique challenge to the signatories that depends on the exact action being performed.
        // If the signatures are missing or fail validation contract execution will stop here.
        this.VerifySignatures(primaryGroup, signatures, $"{nameof(AddSignatory)}(Nonce:{nonce},Group:{group},Address:{address},NewSize:{newSize},NewQuorum:{newQuorum})");

        System.Array.Resize(ref signatories, signatories.Length + 1);
        signatories[signatories.Length - 1] = address;

        this.SetSignatories(group, signatories);
        this.SetQuorum(group, newQuorum);
        this.SetGroupNonce(group, nonce + 1);
    }

    public void RemoveSignatory(byte[] signatures, string group, Address address, uint newSize, uint newQuorum)
    {
        Assert(!string.IsNullOrEmpty(group));
        Assert(newSize >= newQuorum, "The number of signatories can't be less than the quorum.");

        Address[] prevSignatories = this.GetSignatories(group);
        Address[] signatories = new Address[prevSignatories.Length - 1];

        int i = 0;
        foreach (Address item in prevSignatories)
        {
            if (item == address)
            {
                continue;
            }

            Assert(signatories.Length != i, "The signatory does not exist.");

            signatories[i++] = item;
        }

        Assert(newSize == signatories.Length, "The expected size is incorrect.");

        // The nonce is used to prevent replay attacks.
        uint nonce = this.GetGroupNonce(group);

        // Validate or provide a unique challenge to the signatories that depends on the exact action being performed.
        // If the signatures are missing or fail validation contract execution will stop here.
        this.VerifySignatures(primaryGroup, signatures, $"{nameof(RemoveSignatory)}(Nonce:{nonce},Group:{group},Address:{address},NewSize:{newSize},NewQuorum:{newQuorum})");

        this.SetSignatories(group, signatories);
        this.SetQuorum(group, newQuorum);
        this.SetGroupNonce(group, nonce + 1);
    }
}