namespace Stratis.SmartContracts
{
    public interface IBlock
    {
        /// <summary>
        /// The coinbase address of the current block. This is the address that receives the mining award for this block.
        /// </summary>
        Address Coinbase { get; }

        /// <summary>
        /// The height of the current block.
        /// </summary>
        ulong Number { get; }
    }
}