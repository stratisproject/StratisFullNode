using System.Linq;
using NBitcoin;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR;

/// <summary>
/// This contract is used to maintain the list of multisig members and quorum requirement for a given multisig federation.
/// </summary>
[EmbeddedContract(EmbeddedContractType.Multisig)]
public class MultiSig : SmartContract
{
    const string primaryGroup = "main";
    private readonly uint version;
    private readonly Authentication authentication;

    /// <summary>
    /// The contract used this value to determine whether once-off initialization had been performed.
    /// </summary>
    private bool Initialized
    {
        get => this.State.GetBool("Initialized");
        set => this.State.SetBool("Initialized", value);
    }

    /// <summary>
    /// Constructor that is called by the framework prior to each method call.
    /// </summary>
    /// <param name="state">The smart contract state.</param>
    /// <param name="persistenceStrategy">The persistence strategy used to synthesize states for "external" contract calls.</param>
    /// <param name="network">The network required by the authentication contract and to obtain the initial list of federation members.</param>
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

    /// <summary>
    /// Builds the state for calling another contract from this contract's state but replacing the IPersistentState object.
    /// </summary>
    /// <param name="state">The state of this contract.</param>
    /// <param name="persistenceStrategy">Required for building a PersistentState for the other contact.</param>
    /// <param name="address">The address of the other contract.</param>
    /// <returns>The state for calling another contract with the appropriate IPersistentState object.</returns>
    private ISmartContractState GetState(ISmartContractState state, IPersistenceStrategy persistenceStrategy, uint160 address)
    {
        return new SmartContractState(state.Block, state.Message, new PersistentState(persistenceStrategy, state.Serializer, address),
            state.Serializer, state.ContractLogger, state.InternalTransactionExecutor, state.InternalHashHelper, state.GetBalance);
    }

    /// <summary>
    /// Verifies that the provided signature solves the given authorization challenge.
    /// </summary>
    /// <param name="group">The name of the signatory group that should provide a quorum of signatures.</param>
    /// <param name="signatures">The signatures provided.</param>
    /// <param name="authorizationChallenge">The challenge to sign.</param>
    private void VerifySignatures(string group, byte[] signatures, string authorizationChallenge)
    {
        this.authentication.VerifySignatures(group, signatures, authorizationChallenge);
    }

    /// <summary>
    /// Adds a multisig federation with its public keys and quorum requirement.
    /// </summary>
    /// <param name="federationId">The multisig federation identifier.</param>
    /// <param name="quorum">The quorum requirement of the multisig federation.</param>
    /// <param name="pubKeys">The public keys of the multisig federation.</param>
    private void AddFederation(string federationId, uint quorum, string[] pubKeys)
    {
        SetFederationMembers(federationId, pubKeys);
        SetFederationQuorum(federationId, quorum);
    }

    /// <summary>
    /// Retrieves the public keys of a multisig federation.
    /// </summary>
    /// <param name="federationId">The multisig federation identifier.</param>
    /// <returns>The public keys of the requested multisig federation.</returns>
    public string[] GetFederationMembers(string federationId)
    {
        Assert(!string.IsNullOrEmpty(federationId));
        return this.State.GetArray<string>($"Members:{federationId}");
    }

    /// <summary>
    /// Stores the public keys of a multisig federation.
    /// </summary>
    /// <param name="federationId">The multisig federation identifier.</param>
    /// <param name="values">The public keys of the multisig federation.</param>
    /// <remarks>Note that <see cref="AddMember"/> and <see cref="RemoveMember"/> should be used to modify the federation and hence this method is private.</remarks>
    private void SetFederationMembers(string federationId, string[] values)
    {
        this.State.SetArray($"Members:{federationId}", values);
    }

    /// <summary>
    /// Retrieves the quorum of a multisig federation.
    /// </summary>
    /// <param name="federationId">The multisig federation identifier.</param>
    /// <returns>The quorum of the multisig federation.</returns>
    public uint GetFederationQuorum(string federationId)
    {
        Assert(!string.IsNullOrEmpty(federationId));
        return this.State.GetUInt32($"Quorum:{federationId}");
    }

    /// <summary>
    /// Stores the quorum of a multisig federation.
    /// </summary>
    /// <param name="federationId">The multisig federation identifier.</param>
    /// <param name="quorum">The quorum of the multisig federation.</param>
    /// <remarks>Note that <see cref="AddMember"/> and <see cref="RemoveMember"/> should be used to modify the federation (and quorum) and hence this method is private.</remarks>
    private void SetFederationQuorum(string federationId, uint quorum)
    {
        this.State.SetUInt32($"Quorum:{federationId}", quorum);
    }

    /// <summary>
    /// Retrieves the next unique value to add to authentication challenges for the given federation.
    /// </summary>
    /// <param name="federationId">The multisig federation identifier.</param>
    /// <returns>The unique nonce value.</returns>
    private uint GetFederationNonce(string federationId)
    {
        return this.State.GetUInt32($"Nonce:{federationId}");
    }

    /// <summary>
    /// Stores the next unique value to add to authentication challenges for the given federation.
    /// </summary>
    /// <param name="federationId">The multisig federation identifier.</param>
    /// <param name="value">The unique nonce value.</param>
    private void SetFederationNonce(string federationId, uint value)
    {
        this.State.SetUInt32($"Nonce:{federationId}", value);
    }

    /// <summary>
    /// Updates an array of signatories by appending a public key.
    /// </summary>
    /// <param name="signatories">The list of signatories to update.</param>
    /// <param name="pubKey">The public key to append.</param>
    /// <param name="newSize">The new expected size of the array.</param>
    /// <returns>An updated array of signatories with the public key appended.</returns>
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

    /// <summary>
    /// Adds a member (public key) and new quorum requirement to the specified federation.
    /// </summary>
    /// <param name="signatures">The signatures authenticating this method call.</param>
    /// <param name="federationId">The multisig federation identifier.</param>
    /// <param name="pubKey">The member (public key) to add.</param>
    /// <param name="newSize">The new expected size of the federation.</param>
    /// <param name="newQuorum">The new quorum requirement.</param>
    /// <remarks>The <paramref name="newSize"/> and <paramref name="newQuorum"/> values are required as a safeguard against issues arising due to asynchronous updates.</remarks>
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

    /// <summary>
    /// Updates an array of signatories by removing a public key.
    /// </summary>
    /// <param name="signatories">The list of signatories to update.</param>
    /// <param name="pubKey">The public key to remove.</param>
    /// <param name="newSize">The new expected size of the array.</param>
    /// <returns>An updated array of signatories with the public key removed.</returns>
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

    /// <summary>
    /// Removes a member (public key) and specifies the new quorum requirement to the specified federation.
    /// </summary>
    /// <param name="signatures">The signatures authenticating this method call.</param>
    /// <param name="federationId">The multisig federation identifier.</param>
    /// <param name="pubKey">The member (public key) to remove.</param>
    /// <param name="newSize">The new expected size of the federation.</param>
    /// <param name="newQuorum">The new quorum requirement.</param>
    /// <remarks>The <paramref name="newSize"/> and <paramref name="newQuorum"/> values are required as a safeguard against issues arising due to asynchronous updates.</remarks>
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