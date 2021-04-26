using Stratis.SmartContracts;
using ECRecover = Stratis.SCL.Crypto.ECRecover;

public class MultiSig : SmartContract
{
    private readonly uint version;

    public MultiSig(ISmartContractState state, uint version) : base(state)
    {
        this.version = version;
    }
}

