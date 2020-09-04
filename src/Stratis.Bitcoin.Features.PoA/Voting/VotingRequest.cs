using NBitcoin;

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
        string CollateralMainchainAddress { get; }

        /// <summary>
        /// The signature which signs the hex representation of the voting request transaction hash with the collateral address's private key.
        /// </summary>
        string Signature { get; }
    }

    /// <inheritdoc />
    public class VotingRequest : IVotingRequest
    {
        private PubKey pubKey;
        private Money collateralAmount;
        private string colllateralMainchainAddress;
        private string signature;

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

        public string SignatureMessage => $"The address '{this.colllateralMainchainAddress}' is owned by '{this.PubKey.ToHex()}'";

        /// <inheritdoc />
        public string CollateralMainchainAddress 
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
        public VotingRequest(PubKey pubKey, Money collateralAmount, string collateralMainchainAddress)
        {
            this.PubKey = pubKey;
            this.CollateralAmount = collateralAmount;
            this.CollateralMainchainAddress = collateralMainchainAddress;
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
            stream.ReadWrite(ref this.pubKey);
            if (stream.Serializing)
            {
                ulong amount = this.collateralAmount;
                stream.ReadWrite(ref amount);
            }
            else
            {
                ulong amount = 0;
                stream.ReadWrite(ref amount);
                this.collateralAmount = amount;
            }

            stream.ReadWrite(ref this.colllateralMainchainAddress);
        }
    }
}
