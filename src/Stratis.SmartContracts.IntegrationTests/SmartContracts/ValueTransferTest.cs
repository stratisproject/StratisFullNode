using Stratis.SmartContracts;

public class ValueTransferTest : SmartContract
{
    public ValueTransferTest(ISmartContractState state) : base(state) { }

    public void CanReceiveValue()
    {
        Assert(Message.Value > 0);
    }

    public void CanForwardValueCall(Address other)
    {
        Assert(Message.Value > 0);

        var result = Call(other, Message.Value, "CanReceiveValue");

        Assert(result.Success);
    }
}