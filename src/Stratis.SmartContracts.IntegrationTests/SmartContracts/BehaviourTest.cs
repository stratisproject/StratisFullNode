using Stratis.SmartContracts;

public class BehaviourTest : SmartContract
{
    public BehaviourTest(ISmartContractState contractState) : base(contractState)
    {
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

    public void PersistEmptyString()
    {
        // Store serialized empty string which uses Encoding.UTF8.GetBytes under the hood
        var serializedEmptyString = Serializer.Serialize(string.Empty);

        Assert(serializedEmptyString != null, "Empty string is null!");
        Assert(serializedEmptyString.Length == 0, "Empty string is a non-zero length byte array!");

        // Put the empty string in persistent state.
        this.Data = serializedEmptyString;

        // Test it again. Should succeed.
        DataIsByte0();
    }

    public void PersistNull()
    {
        // Put the null in persistent state.
        this.Data = null;

        // Test it again. Should succeed.
        DataIsByte0();
    }

    public void DataIsByte0()
    {
        Assert(this.Data != null, "Empty string is null!");
        Assert(this.Data.Length == 0, "Empty string is a non-zero length byte array!");
    }

}
