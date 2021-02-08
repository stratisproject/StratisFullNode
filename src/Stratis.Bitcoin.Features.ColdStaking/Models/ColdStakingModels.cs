using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using Stratis.Bitcoin.Utilities.ValidationAttributes;

namespace Stratis.Bitcoin.Features.ColdStaking.Models
{
    /// <summary>
    /// The data structure used by a client to obtain information related to cold staking.
    /// Refer to <see cref="Controllers.ColdStakingController.GetColdStakingInfo"/>.
    /// </summary>
    public class GetColdStakingInfoRequest
    {
        /// <summary>The wallet name.</summary>
        [Required]
        [JsonProperty(PropertyName = "walletName")]
        public string WalletName { get; set; }

        /// <summary>Creates a string containing the properties of this object.</summary>
        /// <returns>A string containing the properties of the object.</returns>
        public override string ToString()
        {
            return $"{nameof(this.WalletName)}={this.WalletName}";
        }
    }

    /// <summary>
    /// The data structure received by a client obtaining information relating to cold staking.
    /// Refer to <see cref="GetColdStakingInfoRequest"/>.
    /// </summary>
    public class GetColdStakingInfoResponse
    {
        /// <summary>Set if the cold wallet account exists.</summary>
        [JsonProperty(PropertyName = "coldWalletAccountExists")]
        public bool ColdWalletAccountExists { get; set; }

        /// <summary>Set if the hot wallet account exists.</summary>
        [JsonProperty(PropertyName = "hotWalletAccountExists")]
        public bool HotWalletAccountExists { get; set; }

        /// <summary>Creates a string containing the properties of this object.</summary>
        /// <returns>A string containing the properties of the object.</returns>
        public override string ToString()
        {
            return $"{nameof(this.ColdWalletAccountExists)}={this.ColdWalletAccountExists},{nameof(this.HotWalletAccountExists)}={this.HotWalletAccountExists}";
        }
    }

    /// <summary>
    /// The data structure used by a client creating a cold staking account.
    /// Refer to <see cref="Controllers.ColdStakingController.CreateColdStakingAccount"/>.
    /// </summary>
    public class CreateColdStakingAccountRequest
    {
        /// <summary>The wallet name.</summary>
        [Required]
        [JsonProperty(PropertyName = "walletName")]
        public string WalletName { get; set; }

        /// <summary>The wallet password.</summary>
        [Required]
        [JsonProperty(PropertyName = "walletPassword")]
        public string WalletPassword { get; set; }

        /// <summary>Determines from which of the cold staking accounts the address will be taken.</summary>
        [Required]
        [JsonProperty(PropertyName = "isColdWalletAccount")]
        public bool IsColdWalletAccount { get; set; }

        /// <summary>If the cold staking account is being added to an extpubkey wallet, the extpubkey of the cold staking account is required.</summary>
        [JsonProperty(PropertyName = "extPubKey")]
        public string ExtPubKey { get; set; }

        /// <summary>Creates a string containing the properties of this object.</summary>
        /// <returns>A string containing the properties of the object.</returns>
        public override string ToString()
        {
            return $"{nameof(this.WalletName)}={this.WalletName},{nameof(this.IsColdWalletAccount)}={this.IsColdWalletAccount}";
        }
    }

    /// <summary>
    /// The response data structure received by a client creating a cold staking account.
    /// Refer to <see cref="CreateColdStakingAccountRequest"/>.
    /// </summary>
    public class CreateColdStakingAccountResponse
    {
        /// <summary>The name of the account that was created or perhaps already existed.</summary>
        [JsonProperty(PropertyName = "accountName")]
        public string AccountName { get; set; }

        /// <summary>Creates a string containing the properties of this object.</summary>
        /// <returns>A string containing the properties of the object.</returns>
        public override string ToString()
        {
            return $"{nameof(this.AccountName)}={this.AccountName}";
        }
    }

    /// <summary>
    /// The data structure used by a client requesting a cold staking address.
    /// Refer to <see cref="Controllers.ColdStakingController.GetColdStakingAddress"/>.
    /// </summary>
    public class GetColdStakingAddressRequest
    {
        /// <summary>The wallet name.</summary>
        [Required]
        [JsonProperty(PropertyName = "walletName")]
        public string WalletName { get; set; }

        /// <summary>Determines from which of the cold staking accounts the address will be taken.</summary>
        [Required]
        [JsonProperty(PropertyName = "isColdWalletAddress")]
        public bool IsColdWalletAddress { get; set; }

        /// <summary>
        /// Whether to return the P2WPKH (segwit bech32) addresses, or a regular P2PKH address
        /// </summary>
        public bool Segwit { get; set; }

        /// <summary>Creates a string containing the properties of this object.</summary>
        /// <returns>A string containing the properties of the object.</returns>
        public override string ToString()
        {
            return $"{nameof(this.WalletName)}={this.WalletName},{nameof(this.IsColdWalletAddress)}={this.IsColdWalletAddress}";
        }
    }

    /// <summary>
    /// The response data structure received by a client after requesting a cold staking address.
    /// Refer to <see cref="GetColdStakingAddressRequest"/>.
    /// </summary>
    public class GetColdStakingAddressResponse
    {
        /// <summary>A Base58 cold staking address from the hot or cold wallet accounts.</summary>
        [JsonProperty(PropertyName = "address")]
        public string Address { get; set; }

        /// <summary>Creates a string containing the properties of this object.</summary>
        /// <returns>A string containing the properties of the object.</returns>
        public override string ToString()
        {
            return $"{nameof(this.Address)}={this.Address}";
        }
    }

    /// <summary>
    /// The data structure used by a client requesting that a cold staking cancellation be performed.
    /// Refer to <see cref="Controllers.ColdStakingController.ColdStakingWithdrawal"/>.
    /// </summary>
    public class BaseColdStakingWithdrawalRequest
    {
        /// <summary>The Base58 receiving address.</summary>
        [Required]
        [JsonProperty(PropertyName = "receivingAddress")]
        public string ReceivingAddress { get; set; }

        /// <summary>The name of the wallet from which we select coins for cold staking cancellation.</summary>
        [Required]
        [JsonProperty(PropertyName = "walletName")]
        public string WalletName { get; set; }

        /// <summary>The amount of coins selected for cold staking cancellation.</summary>
        [Required]
        [MoneyFormat(ErrorMessage = "The amount is not in the correct format.")]
        [JsonProperty(PropertyName = "amount")]
        public string Amount { get; set; }

        [JsonProperty(PropertyName = "subtractFeeFromAmount")]
        public bool SubtractFeeFromAmount { get; set; }

        /// <summary>Creates a string containing the properties of this object.</summary>
        /// <returns>A string containing the properties of the object.</returns>
        public override string ToString()
        {
            return $"{nameof(this.ReceivingAddress)}={this.ReceivingAddress},{nameof(this.WalletName)}={this.WalletName},{nameof(this.Amount)}={this.Amount}";
        }
    }

    public class ColdStakingWithdrawalRequest : BaseColdStakingWithdrawalRequest
    {
        /// <summary>The password of the wallet from which we select coins for cold staking cancellation.</summary>
        [Required]
        [JsonProperty(PropertyName = "walletPassword")]
        public string WalletPassword { get; set; }

        /// <summary>The fees for the cold staking cancellation transaction.</summary>
        [MoneyFormat(ErrorMessage = "The fees are not in the correct format.")]
        [JsonProperty(PropertyName = "fees")]
        public string Fees { get; set; }
    }

    public class OfflineColdStakingWithdrawalFeeEstimationRequest : BaseColdStakingWithdrawalRequest
    {
        /// <summary>The hot account of the watch only wallet from which we select coins for cold staking cancellation.</summary>
        /// <remarks>Note that this is specifically the hot account, which is sufficient for determining UTXOs and also is presumed to be available if the node was staking with it.</remarks>
        [Required]
        [JsonProperty(PropertyName = "accountName")]
        public string AccountName { get; set; }
    }

    public class OfflineColdStakingWithdrawalRequest : OfflineColdStakingWithdrawalFeeEstimationRequest
    {
        /// <summary>The fees for the cold staking cancellation transaction.</summary>
        [MoneyFormat(ErrorMessage = "The fees are not in the correct format.")]
        [JsonProperty(PropertyName = "fees")]
        public string Fees { get; set; }
    }

    /// <summary>
    /// The response data structure received by a client after requesting that a cold staking cancellation be performed.
    /// Refer to <see cref="ColdStakingWithdrawalRequest"/>.
    /// </summary>
    public class ColdStakingWithdrawalResponse
    {
        /// <summary>The transaction bytes as a hexadecimal string.</summary>
        [JsonProperty(PropertyName = "transactionHex")]
        public string TransactionHex { get; set; }

        /// <summary>Creates a string containing the properties of this object.</summary>
        /// <returns>A string containing the properties of the object.</returns>
        public override string ToString()
        {
            return $"{nameof(this.TransactionHex)}={this.TransactionHex}";
        }
    }

    public class BaseSetupColdStakingRequest
    {
        /// <summary>The Base58 cold wallet address.</summary>
        [Required]
        [JsonProperty(PropertyName = "coldWalletAddress")]
        public string ColdWalletAddress { get; set; }

        /// <summary>The Base58 hot wallet address.</summary>
        [Required]
        [JsonProperty(PropertyName = "hotWalletAddress")]
        public string HotWalletAddress { get; set; }

        /// <summary>The name of the wallet from which we select coins for cold staking.</summary>
        [Required]
        [JsonProperty(PropertyName = "walletName")]
        public string WalletName { get; set; }

        /// <summary>The wallet account from which we select coins for cold staking.</summary>
        [Required]
        [JsonProperty(PropertyName = "walletAccount")]
        public string WalletAccount { get; set; }

        /// <summary>The amount of coins selected for cold staking.</summary>
        [Required]
        [MoneyFormat(ErrorMessage = "The amount is not in the correct format.")]
        [JsonProperty(PropertyName = "amount")]
        public string Amount { get; set; }

        /// <summary>The fees for the cold staking setup transaction.</summary>
        [Required]
        [MoneyFormat(ErrorMessage = "The fees are not in the correct format.")]
        [JsonProperty(PropertyName = "fees")]
        public string Fees { get; set; }

        [JsonProperty(PropertyName = "subtractFeeFromAmount")]
        public bool SubtractFeeFromAmount { get; set; }

        [JsonProperty(PropertyName = "splitCount")]
        public int SplitCount { get; set; }

        /// <summary>
        /// Whether to send the change to a P2WPKH (segwit bech32) addresses, or a regular P2PKH address
        /// </summary>
        public bool SegwitChangeAddress { get; set; }

        public BaseSetupColdStakingRequest()
        {
            this.SplitCount = 1;
        }
    }

    /// <summary>
    /// The data structure used by a client requesting that a cold staking setup be performed.
    /// Refer to <see cref="Controllers.ColdStakingController.SetupColdStaking"/>.
    /// </summary>
    public class SetupColdStakingRequest : BaseSetupColdStakingRequest
    {
        /// <summary>The password of the wallet from which we select coins for cold staking.</summary>
        [Required]
        [JsonProperty(PropertyName = "walletPassword")]
        public string WalletPassword { get; set; }

        /// <summary>Creates a string containing the properties of this object.</summary>
        /// <returns>A string containing the properties of the object.</returns>
        public override string ToString()
        {
            return $"{nameof(this.ColdWalletAddress)}={this.ColdWalletAddress},{nameof(this.HotWalletAddress)}={this.HotWalletAddress},{nameof(this.WalletName)}={this.WalletName},{nameof(this.WalletAccount)}={this.WalletAccount},{nameof(this.Amount)}={this.Amount},{nameof(this.Fees)}={this.Fees}";
        }
    }

    public class SetupOfflineColdStakingRequest : BaseSetupColdStakingRequest
    {
    }

    /// <summary>
    /// The response data structure received by a client after requesting that a cold staking setup be performed.
    /// Refer to <see cref="SetupColdStakingRequest"/>.
    /// </summary>
    public class SetupColdStakingResponse
    {
        /// <summary>The transaction bytes as a hexadecimal string.</summary>
        [JsonProperty(PropertyName = "transactionHex")]
        public string TransactionHex { get; set; }

        /// <summary>Creates a string containing the properties of this object.</summary>
        /// <returns>A string containing the properties of the object.</returns>
        public override string ToString()
        {
            return $"{nameof(this.TransactionHex)}={this.TransactionHex}";
        }
    }
}
