namespace Stratis.Bitcoin.Features.Wallet.Interfaces
{
    public class AddressIdentifier
    {
        public int WalletId { get; set; }

        public int? AccountIndex { get; set; }

        public int? AddressType { get; set; }

        public int? AddressIndex { get; set; }

        public string ScriptPubKey { get; set; }

        /// <summary>P2WPKH scriptPubKey</summary>
        public string Bech32ScriptPubKey { get; set; }

        // TODO: Document how this is distinct from ScriptPubKey. Is it the P2PK scriptPubKey as opposed to ScriptPubKey storing the P2PKH scriptPubKey?
        public string PubKeyScript;

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
