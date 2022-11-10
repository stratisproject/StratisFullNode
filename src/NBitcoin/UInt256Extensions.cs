using System;
using System.Numerics;

namespace NBitcoin
{
    public static class UInt256Extensions
    {
        public static string FormatAsFractionalValue(this uint256 amount, int decimals = 18)
        {
            var amountAsBigInteger = new BigInteger(amount.ToBytes());

            decimal scaledAmount = (decimal)amountAsBigInteger / ((long)Math.Pow(10, decimals));

            string formatted = $"{scaledAmount:F18}";

            return formatted;
        }
    }
}
