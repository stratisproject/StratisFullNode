using Stratis.SmartContracts;
using EcRecover = Stratis.SCL.Crypto.ECRecover;

public class EcRecoverContract : SmartContract
{
    public Address ThirdPartySigner
    {
        get
        {
            return this.State.GetAddress(nameof(this.ThirdPartySigner));
        }
        set
        {
            this.State.SetAddress(nameof(this.ThirdPartySigner), value);
        }
    }

    public EcRecoverContract(ISmartContractState state, Address thirdPartySigner) : base(state)
    {
        this.ThirdPartySigner = thirdPartySigner;
    }

    public bool CheckThirdPartySignature(byte[] message, byte[] signature)
    {
        EcRecover.TryGetSigner(message, signature, out Address signerOfMessage);
        return (signerOfMessage == this.ThirdPartySigner);
    }
}