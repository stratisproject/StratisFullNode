using System.Linq;
using NBitcoin;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR;

[EmbeddedContract(EmbeddedContractType.Multisig)]
public class MultiSig : SmartContract
{
    const string primaryGroup = "main";
    private readonly uint version;
    private readonly Authentication authentication;

    private bool Initialized
    {
        get => this.State.GetBool("Initialized");
        set => this.State.SetBool("Initialized", value);
    }

    public MultiSig(ISmartContractState state, IPersistenceStrategy persistenceStrategy, Network network) : base(state)
    {
        uint version = state.Message.ContractAddress.ToUint160().GetEmbeddedVersion();

        Assert(version == 1, "Only a version of 1 is supported.");

        this.version = version;
        this.authentication = new Authentication(GetState(state, persistenceStrategy, EmbeddedContractAddress.Create(typeof(Authentication), 1)), network);

        // Exit if already initialized.
        if (this.Initialized)
            return;

        foreach (IFederation federation in network.Federations.GetFederations())
        {
            (PubKey[] pubKeys, int signaturesRequired) = federation.GetFederationDetails();
            AddFederation(federation.Id.ToHex(network), (uint)signaturesRequired, pubKeys.Select(pk => pk.ToHex()).ToArray());
        }

        this.Initialized = true;
    }

    private ISmartContractState GetState(ISmartContractState state, IPersistenceStrategy persistenceStrategy, uint160 address)
    {
        return new SmartContractState(state.Block, state.Message, new PersistentState(persistenceStrategy, state.Serializer, address),
            state.Serializer, state.ContractLogger, state.InternalTransactionExecutor, state.InternalHashHelper, state.GetBalance);
    }

    private void VerifySignatures(string group, byte[] signatures, string authorizationChallenge)
    {
        this.authentication.VerifySignatures(group, signatures, authorizationChallenge);
    }

    private void AddFederation(string federationId, uint quorum, string[] pubKeys)
    {
        SetFederationMembers(federationId, pubKeys);
        SetFederationQuorum(federationId, quorum);
    }

    public string[] GetFederationMembers(string federationId)
    {
        Assert(!string.IsNullOrEmpty(federationId));
        return this.State.GetArray<string>($"Members:{federationId}");
    }

    private void SetFederationMembers(string federationId, string[] values)
    {
        this.State.SetArray($"Members:{federationId}", values);
    }

    public uint GetFederationQuorum(string federationId)
    {
        Assert(!string.IsNullOrEmpty(federationId));
        return this.State.GetUInt32($"Quorum:{federationId}");
    }

    private void SetFederationQuorum(string federationId, uint quorum)
    {
        this.State.SetUInt32($"Quorum:{federationId}", quorum);
    }

    private uint GetFederationNonce(string federationId)
    {
        return this.State.GetUInt32($"Nonce:{federationId}");
    }

    private void SetFederationNonce(string federationId, uint value)
    {
        this.State.SetUInt32($"Nonce:{federationId}", value);
    }

    private string[] AddSignatory(string[] signatories, string pubKey, uint newSize)
    {
        string[] result = new string[signatories.Length + 1];

        int i = 0;
        foreach (string item in signatories)
        {
            Assert(item != pubKey, "The signatory already exists.");
            result[i++] = item;
        }

        result[i] = pubKey;

        Assert(newSize == result.Length, "The expected size is incorrect.");
        return result;
    }

    public void AddMember(byte[] signatures, string federationId, string pubKey, uint newSize, uint newQuorum)
    {
        Assert(!string.IsNullOrEmpty(federationId));
        Assert(newSize >= newQuorum, "The number of signatories can't be less than the quorum.");
        Assert(new PubKey(pubKey).ToHex().ToUpper() == pubKey.ToUpper());

        string[] signatories = AddSignatory(this.GetFederationMembers(federationId), pubKey, newSize);

        // The nonce is used to prevent replay attacks.
        uint nonce = this.GetFederationNonce(federationId);

        // Validate or provide a unique challenge to the signatories that depends on the exact action being performed.
        // If the signatures are missing or fail validation contract then execution will stop here.
        this.VerifySignatures(primaryGroup, signatures, $"{nameof(AddMember)}(Nonce:{nonce},FederationId:{federationId},PubKey:{pubKey},NewSize:{newSize},NewQuorum:{newQuorum})");

        this.SetFederationMembers(federationId, signatories);
        this.SetFederationQuorum(federationId, newQuorum);
        this.SetFederationNonce(federationId, nonce + 1);
    }

    private string[] RemoveSignatory(string[] signatories, string pubKey, uint newSize)
    {
        string[] result = new string[signatories.Length - 1];

        int i = 0;
        foreach (string item in signatories)
        {
            if (item == pubKey)
            {
                continue;
            }

            Assert(result.Length != i, "The signatory does not exist.");

            result[i++] = item;
        }

        Assert(newSize == result.Length, "The expected size is incorrect.");
        return result;
    }

    public void RemoveMember(byte[] signatures, string federationId, string pubKey, uint newSize, uint newQuorum)
    {
        Assert(!string.IsNullOrEmpty(federationId));
        Assert(newSize >= newQuorum, "The number of signatories can't be less than the quorum.");
        Assert(new PubKey(pubKey).ToHex().ToUpper() == pubKey.ToUpper());

        string[] signatories = RemoveSignatory(this.GetFederationMembers(federationId), pubKey, newSize);

        // The nonce is used to prevent replay attacks.
        uint nonce = this.GetFederationNonce(federationId);

        // Validate or provide a unique challenge to the signatories that depends on the exact action being performed.
        // If the signatures are missing or fail validation then contract execution will stop here.
        this.VerifySignatures(primaryGroup, signatures, $"{nameof(RemoveMember)}(Nonce:{nonce},FederationId:{federationId},PubKey:{pubKey},NewSize:{newSize},NewQuorum:{newQuorum})");

        this.SetFederationMembers(federationId, signatories);
        this.SetFederationQuorum(federationId, newQuorum);
        this.SetFederationNonce(federationId, nonce + 1);
    }
}