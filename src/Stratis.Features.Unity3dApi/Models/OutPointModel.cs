using NBitcoin;

namespace Stratis.Features.Unity3dApi.Models
{
    public class OutPointModel
    {
        public OutPointModel()
        {
        }

        public OutPointModel(OutPoint outPoint)
        {
            this.Hash = outPoint.Hash.ToString();
            this.N = outPoint.N;
        }

        public string Hash;

        public uint N;
    }
}
