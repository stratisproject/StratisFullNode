using Stratis.SmartContracts;

public class ClearDataContract : SmartContract
{
    public ClearDataContract(ISmartContractState contractState) : base(contractState)
    {
        SetData("Test");
    }

    public byte[] Data
    {
        get
        {
            return this.State.GetBytes(nameof(Data));
        }

        private set
        {
            this.State.SetBytes(nameof(Data), value);
        }
    }

    public void ClearData()
    {
        this.State.Clear(nameof(Data));
    }

    public bool ClearDataAndCheck()
    {
        this.State.Clear(nameof(Data));

        return Check();
    }

    public bool Check()
    {
        Assert(Serializer.ToString(this.Data) != "Test", "Value is still test!");

        return this.Data.Length == 0;
    }

    public void SetData(string data)
    {
        this.Data = this.Serializer.Serialize(data);
    }

    public byte[] GetData()
    {
        return this.Data;
    }

    public bool IsDataEmpty()
    {
        return this.Data == null || this.Data.Length == 0;
    }
}
