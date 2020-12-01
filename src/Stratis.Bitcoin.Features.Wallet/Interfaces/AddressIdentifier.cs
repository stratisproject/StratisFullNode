namespace Stratis.Bitcoin.Features.Wallet.Interfaces
{
    public class AddressIdentifier
    {
        public int WalletId { get; set; }

        public int? AccountIndex { get; set; }

        public int? AddressType { get; set; }

        public int? AddressIndex { get; set; }

        /// <summary>The ScriptPubKey of a TxDestination (PKH). TxOut ScriptPubKey values are mapped to one or more of these destinations via ScriptDestinationReader.</summary>
        /// <remarks>Don't use this to store scripts derived from templates other than P2PKH.</remarks>
        public string ScriptPubKey { get; set; }

        /// <summary>Always a P2PK script.</summary>
        public string PubKeyScript { get; set; }

        public override bool Equals(object obj)
        {
            var address = (AddressIdentifier)obj;
            return this.WalletId == address.WalletId &&
                this.AccountIndex == address.AccountIndex &&
                this.AddressType == address.AddressType &&
                this.AddressIndex == address.AddressIndex;
        }

        public override int GetHashCode()
        {
            return (this.WalletId << 16) ^ ((this.AccountIndex ?? 0) << 14) ^ ((this.AddressType ?? 0) << 12) ^ (this.AddressIndex ?? 0);
        }
    }
}
