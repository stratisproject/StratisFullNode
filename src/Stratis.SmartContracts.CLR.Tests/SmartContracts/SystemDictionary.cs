using Stratis.SmartContracts;

public class SystemDictionary : SmartContract
{
    private const string errActionNotAnInitiator = "Not an initiator.";
    private const string errActionInvalidatedDueToChanges = "The action has been invalidated due to parallel updates.";
    private const string errActionInitiatorShouldCancel = "Only the action initiator can cancel it.";
    private const string errActionDoesNotExist = "The action does not exist.";

    public enum ActionTypes
    {
        None,
        WhiteListUpdate,
        DictionaryUpdate
    }

    public struct DictionaryEntry
    {
        public UInt256 CodeHash;
        public Address Address;
    }

    public enum Lookups
    {
        DictByName = 1,
        NonceByHash = 2,
        NonceByName = 3,
        ActionDetails = 4,
        WLByCodeHash = 5,
        ApproversByAction = 6
    }

    public struct DictionaryUpdate
    {
        public string Name;
        public UInt256 CodeHash;
        public Address Address;
        public uint Nonce;
        public Address Owner;
        public uint ExpiryHeight;
    }

    public struct WhiteListUpdate
    {
        public UInt256 CodeHash;
        public bool WhiteListed;
        public uint Nonce;
        public Address Owner;
        public uint ExpiryHeight;
    }
    
    private bool GetIsWhiteListed(UInt256 hash) => this.State.GetBool($"{Lookups.WLByCodeHash}:{hash}");
    private void SetIsWhiteListed(UInt256 hash, bool isWhiteListed) => this.State.SetStruct($"{Lookups.WLByCodeHash}:{hash}", isWhiteListed);
    private DictionaryEntry GetDictionaryEntry(string name) => this.State.GetStruct<DictionaryEntry>($"{Lookups.DictByName}:{name}");
    private void SetDictionaryEntry(string name, DictionaryEntry value) => this.State.SetStruct($"{Lookups.DictByName}:{name}", value);
    private uint GetNonce(UInt256 hash) => this.State.GetUInt32($"{Lookups.NonceByHash}:{hash}");
    private void SetNonce(UInt256 hash, uint nonce) => this.State.SetUInt32($"{Lookups.NonceByHash}:{hash}", nonce);
    private uint GetNonce(string name) => this.State.GetUInt32($"{Lookups.NonceByName}:{name}");
    private void SetNonce(string name, uint nonce) => this.State.SetUInt32($"{Lookups.NonceByName}:{name}", nonce);

    private WhiteListUpdate GetWhiteListUpdate(string actionDescriptor) => this.State.GetStruct<WhiteListUpdate>($"{Lookups.ActionDetails}:{actionDescriptor}");
    private void SetWhiteListUpdate(string actionDescriptor, WhiteListUpdate action) => this.State.SetStruct<WhiteListUpdate>($"{Lookups.ActionDetails}:{actionDescriptor}", action);
    private DictionaryUpdate GetDictionaryUpdate(string actionDescriptor) => this.State.GetStruct<DictionaryUpdate>($"{Lookups.ActionDetails}:{actionDescriptor}");
    private void SetDictionaryUpdate(string actionDescriptor, DictionaryUpdate action) => this.State.SetStruct<DictionaryUpdate>($"{Lookups.ActionDetails}:{actionDescriptor}", action);
    private void ClearAction(string actionDescriptor) => this.State.Clear($"{Lookups.ActionDetails}:{actionDescriptor}");
    private Address[] GetApprovers(string actionDescriptor) => this.State.GetArray<Address>($"{Lookups.ApproversByAction}:{actionDescriptor}");
    private void SetApprovers(string actionDescriptor, Address[] addresses) => this.State.SetArray($"{Lookups.ApproversByAction}:{actionDescriptor}", addresses);
    private void ClearApprovers(string actionDescriptor) => this.State.Clear($"{Lookups.ApproversByAction}:{actionDescriptor}");

    public SystemDictionary(ISmartContractState state) : base(state)
    {
    }

    public bool IsWhiteListed(UInt256 codeHash)
    {
        Assert(codeHash != default(UInt256));

        return this.GetIsWhiteListed(codeHash);
    }

    public UInt256 GetCodeHash(string name)
    {
        Assert(!string.IsNullOrEmpty(name));

        return GetDictionaryEntry(name).CodeHash;
    }

    public Address GetContractAddress(string name)
    {
        Assert(!string.IsNullOrEmpty(name));

        return GetDictionaryEntry(name).Address;
    }

    public string UpdateDictionary(string name, UInt256 codeHash, Address address, uint expiryHeight)
    {
        Assert(!string.IsNullOrEmpty(name));
        //Assert(IsInitiator(nameof(DictionaryUpdate), this.Message.Sender), errActionNotAnInitiator);
        Assert(GetIsWhiteListed(codeHash));
        Assert(expiryHeight > this.Block.Number);

        DictionaryUpdate dictionaryUpdate;

        dictionaryUpdate.Name = name;
        dictionaryUpdate.CodeHash = codeHash;
        dictionaryUpdate.Address = address;
        dictionaryUpdate.Nonce = GetNonce(name);
        dictionaryUpdate.Owner = this.Message.Sender;
        dictionaryUpdate.ExpiryHeight = expiryHeight;

        string actionDescriptor = DictionaryUpdateActionDescriptor(dictionaryUpdate);

        SetDictionaryUpdate(actionDescriptor, dictionaryUpdate);

        return actionDescriptor;
    }

    private string DictionaryUpdateActionDescriptor(DictionaryUpdate dictionaryUpdate)
    {
        return $"{nameof(ActionTypes.DictionaryUpdate)}(Name:{dictionaryUpdate.Name},CodeHash:{dictionaryUpdate.CodeHash},Address:{dictionaryUpdate.Address},Nonce:{dictionaryUpdate.Nonce})";
    }

    private string WhiteListUpdateActionDescriptor(WhiteListUpdate whiteListUpdate)
    {
        return $"{nameof(ActionTypes.WhiteListUpdate)}(CodeHash:{whiteListUpdate.CodeHash},WhiteListed:{whiteListUpdate.WhiteListed},Nonce:{whiteListUpdate.Nonce},Owner:{whiteListUpdate.Owner})";
    }

    public string UpdateWhiteList(UInt256 codeHash, bool whiteListed, uint expiryHeight)
    {
        Assert(codeHash != default(UInt256));
        //Assert(IsInitiator(nameof(WhiteListUpdate), this.Message.Sender), errActionNotAnInitiator);
        Assert(!GetIsWhiteListed(codeHash));
        Assert(expiryHeight > this.Block.Number);

        WhiteListUpdate whiteListUpdate;

        whiteListUpdate.CodeHash = codeHash;
        whiteListUpdate.WhiteListed = whiteListed;
        whiteListUpdate.Nonce = GetNonce(whiteListUpdate.CodeHash);
        whiteListUpdate.Owner = this.Message.Sender;
        whiteListUpdate.ExpiryHeight = expiryHeight;

        string actionDescriptor = WhiteListUpdateActionDescriptor(whiteListUpdate);

        SetWhiteListUpdate(actionDescriptor, whiteListUpdate);

        return actionDescriptor;
    }

    public string ApproveWhiteListUpdate(string actionDescriptor)
    {
        Assert(!string.IsNullOrEmpty(actionDescriptor));

        WhiteListUpdate whiteListUpdate = GetWhiteListUpdate(actionDescriptor);

        Assert(whiteListUpdate.CodeHash != default(UInt256), errActionDoesNotExist);

        if (whiteListUpdate.ExpiryHeight <= this.Block.Number)
        {
            ClearAction(actionDescriptor);
            ClearApprovers(actionDescriptor);
            Assert(false, errActionDoesNotExist);
        }

        string approvalStatus = Approve(actionDescriptor);
        if (IsApprovedStatus(approvalStatus))
        {
            SetIsWhiteListed(whiteListUpdate.CodeHash, whiteListUpdate.WhiteListed);
            SetNonce(whiteListUpdate.CodeHash, whiteListUpdate.Nonce + 1);

            ClearAction(actionDescriptor);
            ClearApprovers(actionDescriptor);
        }

        return approvalStatus;
    }

    public string ApproveDictionaryUpdate(string actionDescriptor)
    {
        Assert(!string.IsNullOrEmpty(actionDescriptor));

        DictionaryUpdate dictionaryUpdate = GetDictionaryUpdate(actionDescriptor);

        Assert(!string.IsNullOrEmpty(dictionaryUpdate.Name), errActionDoesNotExist);

        if (dictionaryUpdate.ExpiryHeight <= this.Block.Number)
        {
            ClearAction(actionDescriptor);
            ClearApprovers(actionDescriptor);
            Assert(false, errActionDoesNotExist);
        }

        string approvalStatus = Approve(actionDescriptor);
        if (IsApprovedStatus(approvalStatus))
        {
            Assert(GetNonce(dictionaryUpdate.Name) == dictionaryUpdate.Nonce, errActionInvalidatedDueToChanges);

            SetDictionaryEntry(dictionaryUpdate.Name, new DictionaryEntry() { Address = dictionaryUpdate.Address, CodeHash = dictionaryUpdate.CodeHash });
            SetNonce(dictionaryUpdate.Name, dictionaryUpdate.Nonce + 1);

            ClearAction(actionDescriptor);
            ClearApprovers(actionDescriptor);
        }

        return approvalStatus;
    }

    public void CancelWhiteListUpdate(string actionDescriptor)
    {
        Assert(!string.IsNullOrEmpty(actionDescriptor));

        WhiteListUpdate whiteListUpdate = GetWhiteListUpdate(actionDescriptor);

        Assert(whiteListUpdate.CodeHash != default(UInt256), errActionDoesNotExist);
        Assert(whiteListUpdate.Owner == this.Message.Sender, errActionInitiatorShouldCancel);

        ClearAction(actionDescriptor);
        ClearApprovers(actionDescriptor);
    }

    public void CancelDictionaryUpdate(string actionDescriptor)
    {
        Assert(!string.IsNullOrEmpty(actionDescriptor));

        DictionaryUpdate dictionaryUpdate = GetDictionaryUpdate(actionDescriptor);

        Assert(!string.IsNullOrEmpty(dictionaryUpdate.Name), errActionDoesNotExist);
        Assert(dictionaryUpdate.Owner == this.Message.Sender, errActionInitiatorShouldCancel);

        ClearAction(actionDescriptor);
        ClearApprovers(actionDescriptor);
    }

    private bool IsApprovedStatus(string approvalStatus) => approvalStatus.StartsWith("Approved");

    private string Approve(string actionDescriptor)
    {
        // Assert(IsApprover(action.GetType().Name, this.Message.Sender), "Not an approver.");

        bool alreadyApproved = false;
        Address[] approvers = GetApprovers(actionDescriptor);
        for (int i = 0; i < approvers.Length; i++)
        {
            if (approvers[i] == this.Message.Sender)
            {
                alreadyApproved = true;
                break;
            }
        }

        if (!alreadyApproved)
        {
            Address[] newApprovers = new Address[approvers.Length + 1];
            newApprovers[0] = this.Message.Sender;
            System.Array.Copy(approvers, 0, newApprovers, 1, approvers.Length);
            SetApprovers(actionDescriptor, newApprovers);
        }

        string approvalStatus = ""; // ApprovalStatus(action.GetType().Name, approvers)

        return approvalStatus;
    }
}