using Stratis.SmartContracts;

[Deploy]
public class LocalCallTests : SmartContract
{
    public LocalCallTests(ISmartContractState state) : base(state)
    {
    }

    public void CreateLog()
    {
        Log(new CalledLog { Name = nameof(CreateLog) });
    }

    public void CreateTransfer()
    {
        Transfer(Address.Zero, 1);
    }

    public struct CalledLog
    {
        public string Name;
    }
}