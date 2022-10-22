using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Stratis.Features.Diagnostic.Utils
{
    /// <summary>
    /// Non locking Concurrent Fixed Size Queue.
    /// This implementation is a lose fixed size queue, because it may sometime exceed the number of items because it wraps a ConcurrentQueue and
    /// that is a lock-free concurrent queue implementation, so even if there is a chance it may exceed maxSize, it serves the purpose of circular buffer
    /// to hold a limited set of updated elements.
    /// </summary>
    /// <typeparam name="T">The type of collection items.</typeparam>
    /// <seealso cref="System.Collections.Generic.IReadOnlyCollection{T}" />
    /// <seealso cref="System.Collections.ICollection" />
    public class ConcurrentFixedSizeQueue<T> : IReadOnlyCollection<T>, ICollection
    {
        private readonly ConcurrentQueue<T> concurrentQueue;
        private readonly int maxSize;

        /// <summary>
        /// Number of items in the queue.
        /// </summary>
        public int Count => this.concurrentQueue.Count;

        /// <summary>
        /// Indicates whether the queue is empty.
        /// </summary>
        public bool IsEmpty => this.concurrentQueue.IsEmpty;

        /// <summary>
        /// Class instance constructor.
        /// </summary>
        /// <param name="maxSize">Initial maximum size.</param>
        public ConcurrentFixedSizeQueue(int maxSize) : this(Array.Empty<T>(), maxSize) { }

        /// <summary>
        /// Class instance constructor.
        /// </summary>
        /// <param name="initialCollection">The intial collection.</param>
        /// <param name="maxSize">Initial maximum size.</param>
        public ConcurrentFixedSizeQueue(IEnumerable<T> initialCollection, int maxSize)
        {
            if (initialCollection == null)
            {
                throw new ArgumentNullException(nameof(initialCollection));
            }

            this.concurrentQueue = new ConcurrentQueue<T>(initialCollection);
            this.maxSize = maxSize;
        }

        /// <summary>
        /// Adds an item to the queue.
        /// </summary>
        /// <param name="item">The item to add to the queue.</param>
        public void Enqueue(T item)
        {
            this.concurrentQueue.Enqueue(item);

            if (this.concurrentQueue.Count > this.maxSize)
            {
                T result;
                this.concurrentQueue.TryDequeue(out result);
            }
        }

        /// <summary>
        /// Tries to return an object from the begginning of the queue without removing it.
        /// </summary>
        /// <param name="result">The object from the beginning of the queue (if any).</param>
        public void TryPeek(out T result) => this.concurrentQueue.TryPeek(out result);

        /// <summary>
        /// Tries to remove an object from the beginning of the queue (if any).
        /// </summary>
        /// <param name="result">The object from the beginning of the queue (if any).</param>
        /// <returns><c>True</c> if an element was removed and <c>false</c> otherwise.</returns>
        public bool TryDequeue(out T result) => this.concurrentQueue.TryDequeue(out result);

        /// <summary>
        /// Copies the queue elements to an array starting at the given index.
        /// </summary>
        /// <param name="array">The array to copy the elements to.</param>
        /// <param name="index">The index in the array to start copying to.</param>
        public void CopyTo(T[] array, int index) => this.concurrentQueue.CopyTo(array, index);

        /// <summary>
        /// Converts the queue elements to an array.
        /// </summary>
        /// <returns>The array containing the queue elements.</returns>
        public T[] ToArray() => this.concurrentQueue.ToArray();

        /// <summary>
        /// Gets an enumerator for iterating the queue elements.
        /// </summary>
        /// <returns>The enumerator for iterating the queue elements.</returns>
        public IEnumerator<T> GetEnumerator() => this.concurrentQueue.GetEnumerator();

        /// <summary>
        /// Gets an enumerator for iterating the queue elements.
        /// </summary>
        /// <returns>The enumerator for iterating the queue elements.</returns>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #region Explicit ICollection implementations.

        /// <summary>
        /// Copies the queue elements to an array starting at the given index.
        /// </summary>
        /// <param name="array">The array to copy the elements to.</param>
        /// <param name="index">The index in the array to start copying to.</param>
        void ICollection.CopyTo(Array array, int index) => ((ICollection)this.concurrentQueue).CopyTo(array, index);

        /// <summary>
        /// Gets an object that can be used to synchronize access to the collection.
        /// </summary>
        object ICollection.SyncRoot => ((ICollection)this.concurrentQueue).SyncRoot;

        /// <summary>
        /// Gets a value indicating whether access to the collection is synchronized (thread-safe).
        /// </summary>
        bool ICollection.IsSynchronized => ((ICollection)this.concurrentQueue).IsSynchronized;
        #endregion

        /// <summary>
        /// Serves as the default hash function.
        /// </summary>
        /// <returns>A hash code for the current object.</returns>
        public override int GetHashCode() => this.concurrentQueue.GetHashCode();

        /// <summary>
        /// Determines if the specified object is equal to the current object.
        /// </summary>
        /// <param name="obj">The specified object.</param>
        /// <returns><c>true</c> if the specified object is equal to the current object; otherwise, <c>false</c>.</returns>
        public override bool Equals(object obj) => this.concurrentQueue.Equals(obj);

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString() => this.concurrentQueue.ToString();
    }
}
