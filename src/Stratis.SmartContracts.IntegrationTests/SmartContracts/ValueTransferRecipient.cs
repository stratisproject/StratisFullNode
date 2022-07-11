using Stratis.SmartContracts;

public class ValueTransferRecipient : SmartContract
{
    public ValueTransferRecipient(ISmartContractState state) : base(state) { }

    public void CanReceiveValue()
    {
        Assert(Message.Value > 0);
    }
}

