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

        public byte[] PendingKeyBytes => ASCIIEncoding.ASCII.GetBytes(this.TransactionId);

        public byte[] KeyBytes => (this.State == OpenBankDepositState.Pending) ? this.PendingKeyBytes :
            ASCIIEncoding.ASCII.GetBytes(this.BookDateTimeUTC.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss") + " " + this.TransactionId);

        public byte[] IndexKeyBytes => new[] { (byte)this.State }.Concat(this.KeyBytes).ToArray();

        public BitcoinAddress ParseAddressFromReference(Network network)
        {
            // The "TransactionReference" must contain a valid network address.
            var candidates = this.Reference.Split(' ');

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
