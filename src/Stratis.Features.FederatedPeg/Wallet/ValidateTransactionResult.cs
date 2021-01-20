namespace Stratis.Features.FederatedPeg.Wallet
{
    public sealed class ValidateTransactionResult
    {
        public bool IsValid { get; set; }
        public string[] Errors { get; set; }

        private ValidateTransactionResult() { }

        public static ValidateTransactionResult Failed(params string[] errors)
        {
            return new ValidateTransactionResult() { Errors = errors };
        }

        public static ValidateTransactionResult Valid()
        {
            return new ValidateTransactionResult() { IsValid = true };
        }
    }
}