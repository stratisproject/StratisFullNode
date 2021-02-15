using System.Collections.Generic;

namespace Stratis.Bitcoin.Features.Interop.Models
{
    public class EthereumRequestModel
    {
        public string RequestId { get; set; }

        public string Account { get; set; }

        public string AccountPassphrase { get; set; }

        public string ContractAddress { get; set; }

        public string MethodName { get; set; }

        public List<string> Parameters { get; set; }
    }
}
