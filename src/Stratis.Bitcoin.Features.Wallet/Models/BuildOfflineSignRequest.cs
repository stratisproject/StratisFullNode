using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Stratis.Bitcoin.Utilities.ValidationAttributes;

namespace Stratis.Bitcoin.Features.Wallet.Models
{
    public class BuildOfflineSignRequest : TxFeeEstimateRequest, IValidatableObject
    {
        // Currently this is similar to a normal transaction build request, but it allows unsigned transactions
        // to be built from a wallet without private keys (i.e. a wallet restored from an extPubKey). Therefore
        // no wallet password is required, as there are no keys to decrypt.

        /// <summary>
        /// The fee for the transaction. It is required to set it explicitly for generating an offline signing template.
        /// </summary>
        [MoneyFormat(isRequired: true, ErrorMessage = "Need to specify feeAmount, or the fee is not in the correct format.")]
        public string FeeAmount { get; set; }

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!string.IsNullOrEmpty(this.FeeType))
            {
                yield return new ValidationResult(
                    $"The query parameters '{nameof(this.FeeAmount)}' and '{nameof(this.FeeType)}' cannot be set at the same time. " +
                    $"Please use '{nameof(this.FeeAmount)}' to manually set the fee for an offline signing request.",
                    new[] { $"{nameof(this.FeeType)}" });
            }
        }
    }
}
