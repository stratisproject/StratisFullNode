using Stratis.Bitcoin.EventBus;

namespace Stratis.Bitcoin.Features.Wallet.Events
{
    /// <summary>
    /// This event is raised when a transaction that relates to an address in the wallet has been received.
    /// <para>
    /// This can happen either via the mempool or when a block is connected (syncing).
    /// </para>
    /// <para>
    /// This ensures that the client UI calls back to the node via the API
    /// to update it's balance.
    /// </para>
    /// </summary>
    public sealed class WalletProcessedTransactionOfInterestEvent : EventBase
    {
    }
}
