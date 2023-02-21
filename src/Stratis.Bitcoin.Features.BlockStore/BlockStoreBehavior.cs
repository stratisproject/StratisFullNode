using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.P2P.Peer;
using Stratis.Bitcoin.P2P.Protocol;
using Stratis.Bitcoin.P2P.Protocol.Behaviors;
using Stratis.Bitcoin.P2P.Protocol.Payloads;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Utilities;
using TracerAttributes;

namespace Stratis.Bitcoin.Features.BlockStore
{
    public interface IBlockStoreBehavior : INetworkPeerBehavior
    {
        bool CanRespondToGetDataPayload { get; set; }

        /// <summary>
        /// Sends information about newly discovered blocks to network peers using "headers" or "inv" message.
        /// </summary>
        /// <param name="blocksToAnnounce">List of chained block headers to announce.</param>
        Task AnnounceBlocksAsync(List<ChainedHeader> blocksToAnnounce);
    }

    public class BlockStoreBehavior : NetworkPeerBehavior, IBlockStoreBehavior
    {
        protected readonly ChainIndexer ChainIndexer;

        protected readonly IConsensusManager consensusManager;
        protected readonly IBlockStoreQueue blockStoreQueue;

        protected ConsensusManagerBehavior consensusManagerBehavior;

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>Factory for creating loggers.</summary>
        protected readonly ILoggerFactory loggerFactory;

        /// <inheritdoc />
        public bool CanRespondToGetDataPayload { get; set; }

        /// <summary>Local resources.</summary>
        /// <remarks>Public for testing.</remarks>
        public bool PreferHeaders;

        private readonly bool preferHeaderAndIDs;

        /// <summary>Chained header of the last header sent to the peer.</summary>
        private ChainedHeader lastSentHeader;

        protected readonly IChainState chainState;

        public BlockStoreBehavior(ChainIndexer chainIndexer, IChainState chainState, ILoggerFactory loggerFactory, IConsensusManager consensusManager, IBlockStoreQueue blockStoreQueue)
        {
            Guard.NotNull(chainIndexer, nameof(chainIndexer));
            Guard.NotNull(loggerFactory, nameof(loggerFactory));
            Guard.NotNull(consensusManager, nameof(consensusManager));
            Guard.NotNull(blockStoreQueue, nameof(blockStoreQueue));

            this.ChainIndexer = chainIndexer;
            this.chainState = chainState;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.loggerFactory = loggerFactory;
            this.consensusManager = consensusManager;
            this.blockStoreQueue = blockStoreQueue;

            this.CanRespondToGetDataPayload = true;

            this.PreferHeaders = false;
            this.preferHeaderAndIDs = false;
        }

        [NoTrace]
        protected override void AttachCore()
        {
            this.AttachedPeer.MessageReceived.Register(this.OnMessageReceivedAsync);
            this.consensusManagerBehavior = this.AttachedPeer.Behavior<ConsensusManagerBehavior>();
        }

        [NoTrace]
        protected override void DetachCore()
        {
            this.AttachedPeer.MessageReceived.Unregister(this.OnMessageReceivedAsync);
        }

        [NoTrace]
        private async Task OnMessageReceivedAsync(INetworkPeer peer, IncomingMessage message)
        {
            try
            {
                await this.ProcessMessageAsync(peer, message).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                this.logger.LogTrace("(-)[CANCELED_EXCEPTION]");
                return;
            }
            catch (Exception ex)
            {
                this.logger.LogError("Exception occurred: {0}", ex.ToString());
                this.logger.LogTrace("(-)[EXCEPTION]");
                throw;
            }
        }

        [NoTrace]
        protected virtual async Task ProcessMessageAsync(INetworkPeer peer, IncomingMessage message)
        {
            switch (message.Message.Payload)
            {
                case GetDataPayload getDataPayload:

                    if (!this.CanRespondToGetDataPayload)
                    {
                        this.logger.LogDebug("Can't respond to 'getdata'.");
                        break;
                    }

                    await this.ProcessGetDataAsync(peer, getDataPayload).ConfigureAwait(false);

                    break;

                case SendHeadersPayload _:
                    this.PreferHeaders = true;
                    break;
            }
        }

        private async Task ProcessGetDataAsync(INetworkPeer peer, GetDataPayload getDataPayload)
        {
            // TODO: bring logic from core
            foreach (InventoryVector item in getDataPayload.Inventory.Where(inv => inv.Type.HasFlag(InventoryType.MSG_BLOCK)))
            {
                // We could just check once on entry into this method, but it is possible for the peer's connected status to change
                // between block transmission attempts.
                if (!peer.IsConnected)
                    continue;

                ChainedHeaderBlock chainedHeaderBlock = this.consensusManager.GetBlockData(item.Hash);

                if (chainedHeaderBlock?.Block != null)
                {
                    this.logger.LogDebug("Sending block '{0}' to peer '{1}'.", chainedHeaderBlock.ChainedHeader, peer.RemoteSocketEndpoint);

                    await peer.SendMessageAsync(new BlockPayload(chainedHeaderBlock.Block.WithOptions(this.ChainIndexer.Network.Consensus.ConsensusFactory, peer.SupportedTransactionOptions))).ConfigureAwait(false);
                }
                else
                {
                    this.logger.LogDebug("Block with hash '{0}' requested from peer '{1}' was not found in store.", item.Hash, peer.RemoteSocketEndpoint);

                    // https://btcinformation.org/en/developer-reference#notfound
                    // https://github.com/bitcoin/bitcoin/pull/2192
                    await peer.SendMessageAsync(new NotFoundPayload(InventoryType.MSG_BLOCK, item.Hash)).ConfigureAwait(false);
                }
            }
        }

        private async Task SendAsBlockInventoryAsync(INetworkPeer peer, List<ChainedHeader> blocks)
        {
            // TODO please don't use queue here. Refactor it.
            var queue = new Queue<InventoryVector>(blocks.Select(s => new InventoryVector(InventoryType.MSG_BLOCK, s.HashBlock)));
            while (queue.Count > 0)
            {
                InventoryVector[] items = queue.TakeAndRemove(ConnectionManager.MaxInventorySize).ToArray();
                if (peer.IsConnected)
                {
                    this.logger.LogDebug("Sending inventory message to peer '{0}'.", peer.RemoteSocketEndpoint);
                    await peer.SendMessageAsync(new InvPayload(items)).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task AnnounceBlocksAsync(List<ChainedHeader> blocksToAnnounce)
        {
            Guard.NotNull(blocksToAnnounce, nameof(blocksToAnnounce));

            if (!blocksToAnnounce.Any())
            {
                this.logger.LogTrace("(-)[NO_BLOCKS]");
                return;
            }

            INetworkPeer peer = this.AttachedPeer;
            if (peer == null)
            {
                this.logger.LogTrace("(-)[NO_PEER]");
                return;
            }

            bool revertToInv = (!this.PreferHeaders && (!this.preferHeaderAndIDs || blocksToAnnounce.Count > 1));

            this.logger.LogDebug("Block propagation preferences of the peer '{0}': prefer headers - {1}, prefer headers and IDs - {2}, will{3} revert to 'inv' now.", peer.RemoteSocketEndpoint, this.PreferHeaders, this.preferHeaderAndIDs, revertToInv ? "" : " NOT");

            var headers = new List<ChainedHeader>();
            var inventoryBlockToSend = new List<ChainedHeader>();

            try
            {
                ChainedHeader bestSentHeader = this.consensusManagerBehavior.BestSentHeader;

                ChainedHeader bestIndex = null;
                if (!revertToInv)
                {
                    bool foundStartingHeader = false;

                    // In case we don't have any information about peer's tip send him only last header and don't update best sent header.
                    // We expect peer to answer with getheaders message.
                    if (bestSentHeader == null)
                    {
                        await peer.SendMessageAsync(this.BuildHeadersAnnouncePayload(new[] { blocksToAnnounce.Last() })).ConfigureAwait(false);

                        this.logger.LogTrace("(-)[SENT_SINGLE_HEADER]");
                        return;
                    }

                    // Try to find first chained block that the peer doesn't have, and then add all chained blocks past that one.
                    foreach (ChainedHeader chainedHeader in blocksToAnnounce)
                    {
                        bestIndex = chainedHeader;

                        if (!foundStartingHeader)
                        {
                            this.logger.LogDebug("Checking is the peer '{0}' can connect header '{1}'.", peer.RemoteSocketEndpoint, chainedHeader);

                            // Peer doesn't have a block at the height of our block and with the same hash?
                            if (bestSentHeader?.FindAncestorOrSelf(chainedHeader) != null)
                            {
                                this.logger.LogDebug("Peer '{0}' already has header '{1}'.", peer.RemoteSocketEndpoint, chainedHeader.Previous);
                                continue;
                            }

                            // Peer doesn't have a block at the height of our block.Previous and with the same hash?
                            if (bestSentHeader?.FindAncestorOrSelf(chainedHeader.Previous) == null)
                            {
                                // Peer doesn't have this header or the prior one - nothing will connect, so bail out.
                                this.logger.LogDebug("Neither the header nor its previous header found for peer '{0}', reverting to 'inv'.", peer.RemoteSocketEndpoint);
                                revertToInv = true;
                                break;
                            }

                            this.logger.LogDebug("Peer '{0}' can connect header '{1}'.", peer.RemoteSocketEndpoint, chainedHeader.Previous);
                            foundStartingHeader = true;
                        }

                        // If we reached here then it means that we've found starting header.
                        headers.Add(chainedHeader);
                    }
                }

                if (!revertToInv && headers.Any())
                {
                    if ((headers.Count == 1) && this.preferHeaderAndIDs)
                    {
                        // TODO:
                    }
                    else if (this.PreferHeaders)
                    {
                        if (headers.Count > 1) this.logger.LogDebug("Sending {0} headers, range {1} - {2}, to peer '{3}'.", headers.Count, headers.First(), headers.Last(), peer.RemoteSocketEndpoint);
                        else this.logger.LogDebug("Sending header '{0}' to peer '{1}'.", headers.First(), peer.RemoteSocketEndpoint);

                        this.lastSentHeader = bestIndex;
                        this.consensusManagerBehavior.UpdateBestSentHeader(this.lastSentHeader);

                        await peer.SendMessageAsync(this.BuildHeadersAnnouncePayload(headers)).ConfigureAwait(false);
                        this.logger.LogTrace("(-)[SEND_HEADERS_PAYLOAD]");
                        return;
                    }
                    else
                    {
                        revertToInv = true;
                    }
                }

                if (revertToInv)
                {
                    // If falling back to using an inv, just try to inv the tip.
                    // The last entry in 'blocksToAnnounce' was our tip at some point in the past.
                    if (blocksToAnnounce.Any())
                    {
                        ChainedHeader chainedHeader = blocksToAnnounce.Last();
                        if (chainedHeader != null)
                        {
                            if ((bestSentHeader == null) || (bestSentHeader.GetAncestor(chainedHeader.Height) == null))
                            {
                                inventoryBlockToSend.Add(chainedHeader);
                                this.logger.LogDebug("Sending inventory hash '{0}' to peer '{1}'.", chainedHeader.HashBlock, peer.RemoteSocketEndpoint);
                            }
                        }
                    }
                }

                if (inventoryBlockToSend.Any())
                {
                    this.lastSentHeader = inventoryBlockToSend.Last();
                    this.consensusManagerBehavior.UpdateBestSentHeader(this.lastSentHeader);

                    await this.SendAsBlockInventoryAsync(peer, inventoryBlockToSend).ConfigureAwait(false);
                    this.logger.LogTrace("(-)[SEND_INVENTORY]");
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                this.logger.LogTrace("(-)[CANCELED_EXCEPTION]");
                return;
            }
        }

        /// <summary>
        /// Builds payload that announces to the peers new blocks that we've connected.
        /// This method can be overridden to return different type of HeadersPayload, e.g. <see cref="ProvenHeadersPayload" />
        /// </summary>
        /// <param name="headers">The headers.</param>
        /// <returns>
        /// The <see cref="HeadersPayload" /> instance to announce to the peer.
        /// </returns>
        protected virtual Payload BuildHeadersAnnouncePayload(IEnumerable<ChainedHeader> headers)
        {
            return new HeadersPayload(headers.Select(b => b.Header));
        }

        [NoTrace]
        public override object Clone()
        {
            var clone = new BlockStoreBehavior(this.ChainIndexer, this.chainState, this.loggerFactory, this.consensusManager, this.blockStoreQueue)
            {
                CanRespondToGetDataPayload = this.CanRespondToGetDataPayload
            };

            return clone;
        }
    }
}
