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
        /// Date (UTC) of deposit.
        /// </summary>
        public DateTime DateTimeUTC;

        /// <summary>
        /// External Id of deposit such as the bank deposit id.
        /// </summary>
        public string ExternalId;

        /// <summary>
        /// The destination address for the funds.
        /// </summary>
        public string Reference;

        /// <summary>
        /// The amount deposited.
        /// </summary>
        public Money Amount;

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
            long ticks = this.DateTimeUTC.Ticks;
            stream.ReadWrite(ref ticks);
            this.DateTimeUTC = new DateTime(ticks);
            stream.ReadWrite(ref this.ExternalId);
            stream.ReadWrite(ref this.Reference);
            long amount = this.Amount.Satoshi;
            stream.ReadWrite(ref amount);
            this.Amount = new Money(amount);
            byte state = (byte)this.State;
            stream.ReadWrite(ref state);
            this.State = (OpenBankDepositState)state;
            stream.ReadWrite(ref this.TxId);
            stream.ReadWrite(ref this.Block);
        }

        public byte[] KeyBytes => ASCIIEncoding.ASCII.GetBytes(this.ExternalId);

        public byte[] IndexKeyBytes => new[] { (byte)this.State }.Concat(ASCIIEncoding.ASCII.GetBytes(this.ExternalId)).ToArray();
    }
}
