using System;
using NBitcoin;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.PoA.Features.Voting
{
    /// <summary>
    /// Holds the information required to service a voting request that adds this miner as a collateral federation member.
    /// </summary>
    public interface IVotingRequest : IBitcoinSerializable
    {
        /// <summary>
        /// The public key to be associated with this miner on the sidechain.
        /// </summary>
        PubKey PubKey { get; }

        /// <summary>
        /// The collateral amount.
        /// </summary>
        Money CollateralAmount { get; }

        /// <summary>
        /// The address on the main chain that holds the collateral.
        /// </summary>
        KeyId CollateralMainchainAddress { get; }

        /// <summary>
        /// The signature which signs the hex representation of the voting request transaction hash with the collateral address's private key.
        /// </summary>
        string Signature { get; }

        /// <summary>
        /// The identifier for the event that previously removed this miner (if any). Guards against replaying of voting requests.
        /// </summary>
        Guid RemovalEventId { get; }
    }

    /// <inheritdoc />
    public class VotingRequest : IVotingRequest
    {
        private PubKey pubKey;
        private Money collateralAmount;
        private KeyId colllateralMainchainAddress;
        private string signature;
        private Guid removalEventId;

        /// <inheritdoc />
        public PubKey PubKey
        {
            get { return this.pubKey; }
            set { this.pubKey = value; }
        }

        /// <inheritdoc />
        public Money CollateralAmount 
        { 
            get { return this.collateralAmount; }
            private set { this.collateralAmount = value; }
        }

        /// <inheritdoc />
        public string Signature
        {
            get { return this.signature; }
            private set { this.signature = value; }
        }

        /// <inheritdoc />
        public Guid RemovalEventId
        {
            get { return this.removalEventId; }
            private set { this.removalEventId = value; }
        }

        public string SignatureMessage => $"The address '{this.colllateralMainchainAddress}' is owned by '{this.PubKey.ToHex()} ({this.removalEventId})'";

        /// <inheritdoc />
        public KeyId CollateralMainchainAddress 
        {
            get { return this.colllateralMainchainAddress; }
            private set { this.colllateralMainchainAddress = value; }
        }

        /// <summary>
        /// Constructor for this class.
        /// </summary>
        /// <param name="pubKey">The public key to be associated with this miner on the sidechain.</param>
        /// <param name="collateralAmount">The collateral amount.</param>
        /// <param name="collateralMainchainAddress">The address on the main chain that holds the collateral.</param>
        /// <param name="removalEventId">Identifies to the voting event that led to removal of this miner (if any).</param>
        public VotingRequest(PubKey pubKey, Money collateralAmount, KeyId collateralMainchainAddress, Guid removalEventId = default)
        {
            this.PubKey = pubKey;
            this.CollateralAmount = collateralAmount;
            this.CollateralMainchainAddress = collateralMainchainAddress;
            this.RemovalEventId = removalEventId;
        }

        public VotingRequest()
        {
        }

        public void AddSignature(string signature)
        {
            this.Signature = signature;
        }

        /// <inheritdoc />
        public void ReadWrite(BitcoinStream stream)
        {
            if (stream.Serializing)
            {
                byte[] pubKey = this.pubKey.ToBytes();
                Guard.Assert(pubKey.Length == 33);
                stream.ReadWrite(ref pubKey);
                ulong amount = this.collateralAmount;
                stream.ReadWrite(ref amount);
                byte[] keyId = this.colllateralMainchainAddress.ToBytes();
                stream.ReadWrite(ref keyId);
                byte[] sig = Convert.FromBase64String(this.signature);
                Guard.Assert(sig.Length == 65);
                stream.ReadWrite(ref sig);
                byte[] guid = this.removalEventId.ToByteArray();
                stream.ReadWrite(ref guid);
            }
            else
            {
                byte[] pubKey = new byte[33];
                stream.ReadWrite(ref pubKey);
                this.pubKey = new PubKey(pubKey);

                ulong amount = 0;
                stream.ReadWrite(ref amount);
                this.collateralAmount = amount;

                byte[] keyId = new byte[20];
                stream.ReadWrite(ref keyId);
                this.colllateralMainchainAddress = new KeyId(keyId);

                byte[] sig = new byte[65];
                stream.ReadWrite(ref sig);
                this.signature = Convert.ToBase64String(sig);

                byte[] guid = new byte[16];
                stream.ReadWrite(ref guid);
                this.removalEventId = new Guid(guid);
            }
        }
    }
}
