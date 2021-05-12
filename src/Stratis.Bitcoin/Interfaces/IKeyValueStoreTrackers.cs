using System.Collections.Generic;

namespace Stratis.Bitcoin.Interfaces
{
    public interface IKeyValueStoreTrackers
    {
        /// <summary>
        /// Creates trackers for recording changes.
        /// </summary>
        /// <param name="tables">The tables to create trackers for.</param>
        /// <returns>Trackers for recording changes.</returns>
        Dictionary<string, IKeyValueStoreTracker> CreateTrackers(string[] tables);

        /// <summary>
        /// Called when changes to the database are committed.
        /// </summary>
        /// <param name="trackers">The trackers which were created in <see cref="CreateTrackers"/>.</param>
        /// <remarks>This method is intended be called by the <see cref="KeyValueStoreTransaction"/> class.</remarks>
        void OnCommit(Dictionary<string, IKeyValueStoreTracker> trackers);
    }
}
