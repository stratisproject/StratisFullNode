using System;
using System.Collections.Generic;
using System.Linq;

namespace NBitcoin
{
    /// <summary>
    /// An indexer that provides methods to query the best chain (the chain that is validated by the full consensus rules)
    /// </summary>
    public class ChainIndexer
    {
        /// <summary>Locks access to <see cref="blocksByHeight"/>, <see cref="blocksById"/>.</summary>
        private readonly object lockObject = new object();

        /// <remarks>This object has to be protected by <see cref="lockObject"/>.</remarks>
        private readonly Dictionary<int, ChainedHeader> blocksByHeight;

        /// <remarks>This object has to be protected by <see cref="lockObject"/>.</remarks>
        private readonly Dictionary<uint256, ChainedHeader> blocksById;

        public Network Network { get; }

        /// <summary>
        /// The tip of the best known validated chain.
        /// </summary>
        public virtual ChainedHeader Tip { get; private set; }

        /// <summary>
        /// The tip height of the best known validated chain.
        /// </summary>
        public int Height => this.Tip.Height;

        public ChainedHeader Genesis => this.GetHeader(0);

        public ChainIndexer()
        {
            this.blocksByHeight = new Dictionary<int, ChainedHeader>();
            this.blocksById = new Dictionary<uint256, ChainedHeader>();
        }

        public ChainIndexer(Network network) : base()
        {
            this.Network = network;

            var tip = new ChainedHeader(this.Network.GetGenesis().Header, this.Network.GetGenesis().GetHash(), 0);
            this.AddInternal(tip);

            this.Tip = tip;
        }

        public ChainedHeader SetTip(ChainedHeader chainedHeader)
        {
            lock (this.lockObject)
            {
                ChainedHeader fork = chainedHeader.FindFork(this.Tip);

                if (fork == null)
                    throw new InvalidOperationException("Wrong network");

                foreach (ChainedHeader header in this.Tip.EnumerateToGenesis().TakeWhile(h => h.Height > fork.Height))
                    RemoveInternal(header);

                foreach (ChainedHeader header in chainedHeader.EnumerateToGenesis().TakeWhile(h => h.Height > fork.Height))
                    AddInternal(header);

                this.Tip = chainedHeader;

                return fork;
            }
        }

        /// <summary>
        /// Returns the first chained block header that exists in the chain from the list of block hashes.
        /// </summary>
        /// <param name="hashes">Hash to search for.</param>
        /// <returns>First found chained block header or <c>null</c> if not found.</returns>
        public ChainedHeader FindFork(IEnumerable<uint256> hashes)
        {
            if (hashes == null)
                throw new ArgumentNullException("hashes");

            // Find the first block the caller has in the main chain.
            foreach (uint256 hash in hashes)
            {
                ChainedHeader chainedHeader = this.GetHeader(hash);
                if (chainedHeader != null)
                    return chainedHeader;
            }

            return null;
        }

        /// <summary>
        /// Finds the first chained block header that exists in the chain from the block locator.
        /// </summary>
        /// <param name="locator">The block locator.</param>
        /// <returns>The first chained block header that exists in the chain from the block locator.</returns>
        public ChainedHeader FindFork(BlockLocator locator)
        {
            if (locator == null)
                throw new ArgumentNullException("locator");

            return this.FindFork(locator.Blocks);
        }

        /// <summary>
        /// Enumerate chain block headers after given block hash to genesis block.
        /// </summary>
        /// <param name="blockHash">Block hash to enumerate after.</param>
        /// <returns>Enumeration of chained block headers after given block hash.</returns>
        public IEnumerable<ChainedHeader> EnumerateAfter(uint256 blockHash)
        {
            ChainedHeader block = this.GetHeader(blockHash);

            if (block == null)
                return new ChainedHeader[0];

            return this.EnumerateAfter(block);
        }

        /// <summary>
        /// Enumerates chain block headers from the given chained block header to tip.
        /// </summary>
        /// <param name="block">Chained block header to enumerate from.</param>
        /// <returns>Enumeration of chained block headers from given chained block header to tip.</returns>
        public IEnumerable<ChainedHeader> EnumerateToTip(ChainedHeader block)
        {
            if (block == null)
                throw new ArgumentNullException("block");

            return this.EnumerateToTip(block.HashBlock);
        }

        /// <summary>
        /// Enumerates chain block headers from given block hash to tip.
        /// </summary>
        /// <param name="blockHash">Block hash to enumerate from.</param>
        /// <returns>Enumeration of chained block headers from the given block hash to tip.</returns>
        public IEnumerable<ChainedHeader> EnumerateToTip(uint256 blockHash)
        {
            ChainedHeader block = this.GetHeader(blockHash);
            if (block == null)
                yield break;

            yield return block;

            foreach (ChainedHeader chainedBlock in this.EnumerateAfter(blockHash))
                yield return chainedBlock;
        }

        /// <summary>
        /// Enumerates chain block headers after the given chained block header to genesis block.
        /// </summary>
        /// <param name="block">The chained block header to enumerate after.</param>
        /// <returns>Enumeration of chained block headers after the given block.</returns>
        public virtual IEnumerable<ChainedHeader> EnumerateAfter(ChainedHeader block)
        {
            int i = block.Height + 1;
            ChainedHeader prev = block;

            while (true)
            {
                ChainedHeader b = this.GetHeader(i);
                if ((b == null) || (b.Previous != prev))
                    yield break;

                yield return b;
                i++;
                prev = b;
            }
        }

        /// <summary>
        /// TODO: Make this internal when the component moves to Stratis.Bitcoin
        /// </summary>
        public void Add(ChainedHeader addTip)
        {
            lock (this.lockObject)
            {
                if (this.Tip.HashBlock != addTip.Previous.HashBlock)
                    throw new InvalidOperationException("New tip must be consecutive");

                this.AddInternal(addTip);

                this.Tip = addTip;
            }
        }

        private void AddInternal(ChainedHeader addTip)
        {
            this.blocksById.Add(addTip.HashBlock, addTip);
            this.blocksByHeight.Add(addTip.Height, addTip);
        }

        /// <summary>
        /// TODO: Make this internal when the component moves to Stratis.Bitcoin
        /// </summary>
        public void Remove(ChainedHeader removeTip)
        {
            lock (this.lockObject)
            {
                if (this.Tip.HashBlock != removeTip.HashBlock)
                    throw new InvalidOperationException("Trying to remove item that is not the tip.");

                RemoveInternal(removeTip);

                this.Tip = this.blocksById[removeTip.Previous.HashBlock];
            }
        }

        private void RemoveInternal(ChainedHeader removeTip)
        {
            this.blocksById.Remove(removeTip.HashBlock);
            this.blocksByHeight.Remove(removeTip.Height);
        }

        /// <summary>
        /// Get a <see cref="ChainedHeader"/> based on it's hash.
        /// </summary>
        public virtual ChainedHeader GetHeader(uint256 id)
        {
            lock (this.lockObject)
            {
                ChainedHeader result;
                this.blocksById.TryGetValue(id, out result);
                return result;
            }
        }

        /// <summary>
        /// Get a <see cref="ChainedHeader"/> based on it's height.
        /// </summary>
        public virtual ChainedHeader GetHeader(int height)
        {
            lock (this.lockObject)
            {
                ChainedHeader result;
                this.blocksByHeight.TryGetValue(height, out result);
                return result;
            }
        }

        /// <summary>
        /// Get a <see cref="ChainedHeader"/> based on it's height.
        /// </summary>
        public ChainedHeader this[int key] => this.GetHeader(key);

        /// <summary>
        /// Get a <see cref="ChainedHeader"/> based on it's hash.
        /// </summary>
        public ChainedHeader this[uint256 id] => this.GetHeader(id);

        public override string ToString()
        {
            return this.Tip == null ? "no tip" : this.Tip.ToString();
        }
    }
}