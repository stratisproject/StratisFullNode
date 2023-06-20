using Stratis.SmartContracts;
using Base = Stratis.SCL.Base;

[Deploy]
public class LibraryTest : SmartContract
{
    public LibraryTest(ISmartContractState state) : base(state)
    {
        Base.Operations.Noop();
    }

    public void Exists()
    {
        State.SetBool("Exists", true);
    }
}
