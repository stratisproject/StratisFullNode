namespace Stratis.Bitcoin.Interfaces
{
    public interface IVersionProvider
    {
        /// <summary>
        /// Returns an overridden version, including any -xxx suffix, for the particular implementation.
        /// </summary>
        string GetVersion();

        /// <summary>
        /// Returns an overridden version, exluding any -xxx suffix, for the particular implementation.
        /// </summary>
        string GetVersionNoSuffix();
    }
}