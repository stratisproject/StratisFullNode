using System.Collections.Generic;

namespace Stratis.Features.Unity3dApi.Models
{
    public class GetURXOsResponseModel
    {
        public long BalanceSat;

        public List<OutPointModel> UTXOs;

        public string Reason;
    }
}
