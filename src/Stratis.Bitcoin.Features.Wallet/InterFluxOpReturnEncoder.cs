namespace Stratis.Bitcoin.Features.Wallet
{
    /// <summary>Encodes or decodes InterFlux related data to and from OP_RETURN data.</summary>
    public static class InterFluxOpReturnEncoder
    {
        private static string InterFluxPrefix = "INTER";

        public static string Encode(int destinationChain, string address)
        {
            return InterFluxPrefix + destinationChain + "_" + address;
        }

        public static bool TryDecode(string opReturnData, out int destinationChain, out string address)
        {
            int prefixIndex = opReturnData.IndexOf(InterFluxPrefix);
            int separatorIndex = opReturnData.IndexOf("_");

            destinationChain = -1;
            address = string.Empty;

            if (prefixIndex == -1 || separatorIndex == -1)
                return false;

            if (!int.TryParse(opReturnData.Substring(InterFluxPrefix.Length, separatorIndex - InterFluxPrefix.Length), out destinationChain))
                return false;

            address = opReturnData.Substring(separatorIndex + 1);

            if (string.IsNullOrEmpty(address))
                return false;

            return true;
        }
    }
}
