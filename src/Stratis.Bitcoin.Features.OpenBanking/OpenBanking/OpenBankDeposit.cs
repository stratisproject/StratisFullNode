using System;
using System.Linq;
using System.Text;
using NBitcoin;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.OpenBanking.OpenBanking
{
    public class OpenBankDeposit : IBitcoinSerializable
    {
        /// <summary>
        /// Booking Date (UTC) of deposit.
        /// </summary>
        public DateTime BookDateTimeUTC;

        /// <summary>
        /// Transaction Id of deposit.
        /// </summary>
        public string TransactionId;

        /// <summary>
        /// The destination address for the funds.
        /// </summary>
        public string Reference;

        /// <summary>
        /// The amount deposited.
        /// </summary>
        public Money Amount;

        /// <summary>
        /// When the funds will be available in the bank account.
        /// </summary>
        public DateTime ValueDateTimeUTC;

        /// <summary>
        /// The current state of the deposit.
        /// </summary>
        public OpenBankDepositState State;

        /// <summary>
        /// The id of the transaction minting the token.
        /// </summary>
        public uint256 TxId;

        /// <summary>
        /// The block containing the transaction minting the token.
        /// </summary>
        public HashHeightPair Block;

        public void ReadWrite(BitcoinStream stream)
        {
            long ticks = this.BookDateTimeUTC.ToUniversalTime().Ticks;
            stream.ReadWrite(ref ticks);
            if (!stream.Serializing)
                this.BookDateTimeUTC = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
            stream.ReadWrite(ref this.TransactionId);
            stream.ReadWrite(ref this.Reference);
            long amount = stream.Serializing ? this.Amount.Satoshi : 0;
            stream.ReadWrite(ref amount);
            this.Amount = new Money(amount);
            long ticks2 = this.ValueDateTimeUTC.ToUniversalTime().Ticks;
            stream.ReadWrite(ref ticks2);
            if (!stream.Serializing)
                this.ValueDateTimeUTC = new DateTime(ticks2, DateTimeKind.Utc).ToLocalTime();
            byte state = (byte)this.State;
            stream.ReadWrite(ref state);
            this.State = (OpenBankDepositState)state;
            stream.ReadWrite(ref this.TxId);
            stream.ReadWrite(ref this.Block);
            if (!stream.Serializing && this.Block.Hash == 0)
                this.Block = null;
        }

        /// <summary>The database key used to store deposits when in pending state.</summary>
        /// <remarks>The booking date/time is tentative when in pending state and as such is not used as part of the key when in that state.</remarks>
        public byte[] PendingKeyBytes => ASCIIEncoding.ASCII.GetBytes(this.TransactionId);

        /// <summary>The database key used to store deposits.</summary>
        /// <remarks>The booking date/time is tentative when in pending state and as such is not used as part of the key when in that state.</remarks>
        public byte[] KeyBytes => (this.State == OpenBankDepositState.Pending) ? this.PendingKeyBytes :
            ASCIIEncoding.ASCII.GetBytes(this.BookDateTimeUTC.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss") + " " + this.TransactionId);

        /// <summary>The database key used to index deposits by state.</summary>
        public byte[] IndexKeyBytes => new[] { (byte)this.State }.Concat(this.KeyBytes).ToArray();

        /// <summary>
        /// Parses the target address of the deposit from the banking deposit reference.
        /// </summary>
        /// <param name="network">The network.</param>
        /// <returns>The target address reference if found. Otherwise returns <c>null</c>.</returns>
        public BitcoinAddress ParseAddressFromReference(Network network)
        {
            // Strip out any "adjacent" characters that may have been included to delimit the address.
            var candidates = this.Reference.Split(new char[] { ' ', ',', '.', ':', '"', '\'', '`', '(', ')', '{', '}', '[', ']', '<', '>', '=' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var candidate in candidates)
            {
                try
                {
                    var targetAddress = BitcoinAddress.Create(candidate, network);

                    return targetAddress;
                }
                catch (Exception)
                {
                }
            }

            return null;
        }
    }
}
