using NBitcoin;

namespace Stratis.Bitcoin.Base
{
    /// <summary>
    /// Chain state holds various information related to the status of the chain and its validation.
    /// </summary>
    public interface IChainState
    {
        /// <summary>ChainBehaviors sharing this state will not broadcast headers which are above <see cref="ConsensusTip"/>.</summary>
        ChainedHeader ConsensusTip { get; set; }
    }

    /// <summary>
    /// Chain state holds various information related to the status of the chain and its validation.
    /// The data are provided by different components and the chaine state is a mechanism that allows
    /// these components to share that data without creating extra dependencies.
    /// </summary>
    /// TODO this class should be removed since consensus and block store are moved or about to be moved to base feature
    public class ChainState : IChainState
    {
        /// <inheritdoc />
        public ChainedHeader ConsensusTip { get; set; }
    }
}
