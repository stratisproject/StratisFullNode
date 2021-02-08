using System.Collections.Generic;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.Wallet.Models
{
    public class WalletInfoModel
    {
        public WalletInfoModel()
        {
        }

        public WalletInfoModel(IEnumerable<string> walletNames, IEnumerable<string> watchOnlyWallets)
        {
            this.WalletNames = walletNames;
            this.WatchOnlyWallets = watchOnlyWallets;
        }

        [JsonProperty(PropertyName = "walletNames")]
        public IEnumerable<string> WalletNames { get; set; }

        [JsonProperty(PropertyName = "watchOnlyWallets")]
        public IEnumerable<string> WatchOnlyWallets { get; set; }
    }
}
