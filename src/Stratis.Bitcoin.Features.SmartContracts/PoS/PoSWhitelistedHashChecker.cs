using Stratis.Bitcoin.Features.SmartContracts.Interfaces;

namespace Stratis.Bitcoin.Features.SmartContracts.PoS
{
    /// <summary>
    /// Checks hashes against a whitelist.
    /// </summary>
    public class PoSWhitelistedHashChecker : IWhitelistedHashChecker
    {
        //private readonly IWhitelistedHashesRepository whitelistedHashesRepository;

        public PoSWhitelistedHashChecker(/*IWhitelistedHashesRepository whitelistedHashesRepository*/)
        {
            //this.whitelistedHashesRepository = whitelistedHashesRepository;
        }

        /// <summary>
        /// Checks that a supplied hash is present in the whitelisted hashes repository.
        /// </summary>
        /// <param name="hash">The bytes of the hash to check.</param>
        /// <returns>True if the hash was found in the whitelisted hashes repository.</returns>
        public bool CheckHashWhitelisted(byte[] hash)
        {
            // TODO: For now just allow. Will have to decide how to handle this for system contracts.
            return true;
            /*
            if (hash.Length != 32)
            {
                // For this implementation, only 32 byte wide hashes are accepted.
                return false;
            }

            List<uint256> allowedHashes = this.whitelistedHashesRepository.GetHashes();

            // Now that we've checked the width of the byte array, we don't expect the uint256 constructor to throw any exceptions.
            var hash256 = new uint256(hash);

            return allowedHashes.Contains(hash256);
            */
        }
    }
}