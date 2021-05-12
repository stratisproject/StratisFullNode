namespace Stratis.Bitcoin.Interfaces
{
    /// <summary>
    /// Enumerates the database events to keep track of.
    /// </summary>
    public enum KeyValueStoreEvent
    {
        ObjectCreated,
        ObjectRead,
        ObjectWritten,
        ObjectDeleted
    }

    /// <summary>
    /// Tracks changes made to objects.
    /// </summary>
    public interface IKeyValueStoreTracker
    {
        /// <summary>
        /// Records the object event.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <param name="ev">The event.</param>
        void ObjectEvent(object obj, KeyValueStoreEvent ev);
    }
}
