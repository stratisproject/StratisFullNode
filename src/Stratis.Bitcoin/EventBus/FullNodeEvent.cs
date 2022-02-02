namespace Stratis.Bitcoin.EventBus
{
    /// <summary>
    /// A generic event that can be raised by any component of the full node.
    /// <para>
    /// This can later be extended or abstracted to provide more in-depth information
    /// on a particular event.
    /// </para>
    /// </summary>
    public sealed class FullNodeEvent : EventBase
    {
        public string Message { get; set; }

        public string State { get; set; }
    }
}
