using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Validators;
using Stratis.Bitcoin.Features.Miner;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Mining;
using Stratis.Bitcoin.Utilities;
using TracerAttributes;

namespace Stratis.Bitcoin.Features.PoA
{
    /// <summary>
    /// Mines blocks for PoA network.
    /// Mining can happen only in case this node is a federation member.
    /// </summary>
    /// <remarks>
    /// Blocks can be created only for particular timestamps- once per round.
    /// Round length in seconds is equal to amount of fed members multiplied by target spacing.
    /// Miner's slot in each round is the same and is determined by the index
    /// of current key in <see cref="IFederationManager.GetFederationMembers"/>
    /// </remarks>
    public interface IPoAMiner : IDisposable
    {
        /// <summary>Starts mining loop.</summary>
        void InitializeMining();
    }

    /// <inheritdoc cref="IPoAMiner"/>
    public class PoAMiner : IPoAMiner
    {
        protected readonly IConsensusManager consensusManager;

        private readonly IDateTimeProvider dateTimeProvider;

        protected readonly ILogger logger;

        protected readonly PoANetwork network;

        /// <summary>
        /// A cancellation token source that can cancel the mining processes and is linked to the <see cref="INodeLifetime.ApplicationStopping"/>.
        /// </summary>
        private readonly CancellationTokenSource cancellation;

        private readonly IInitialBlockDownloadState ibdState;

        private readonly BlockDefinition blockDefinition;

        private readonly ISlotsManager slotsManager;

        private readonly IConnectionManager connectionManager;

        private readonly PoABlockHeaderValidator poaHeaderValidator;

        protected readonly IFederationManager federationManager;

        private readonly IIntegrityValidator integrityValidator;

        private readonly IIdleFederationMembersKicker idleFederationMembersKicker;

        private readonly IWalletManager walletManager;

        protected readonly VotingManager votingManager;

        private readonly VotingDataEncoder votingDataEncoder;

        private readonly PoAMinerSettings settings;

        private readonly IAsyncProvider asyncProvider;

        private Task miningTask;

        private Script walletScriptPubKey;

        public PoAMiner(
            IConsensusManager consensusManager,
            IDateTimeProvider dateTimeProvider,
            Network network,
            INodeLifetime nodeLifetime,
            ILoggerFactory loggerFactory,
            IInitialBlockDownloadState ibdState,
            BlockDefinition blockDefinition,
            ISlotsManager slotsManager,
            IConnectionManager connectionManager,
            PoABlockHeaderValidator poaHeaderValidator,
            IFederationManager federationManager,
            IIntegrityValidator integrityValidator,
            IWalletManager walletManager,
            INodeStats nodeStats,
            VotingManager votingManager,
            PoAMinerSettings poAMinerSettings,
            IAsyncProvider asyncProvider,
            IIdleFederationMembersKicker idleFederationMembersKicker)
        {
            this.consensusManager = consensusManager;
            this.dateTimeProvider = dateTimeProvider;
            this.network = network as PoANetwork;
            this.ibdState = ibdState;
            this.blockDefinition = blockDefinition;
            this.slotsManager = slotsManager;
            this.connectionManager = connectionManager;
            this.poaHeaderValidator = poaHeaderValidator;
            this.federationManager = federationManager;
            this.integrityValidator = integrityValidator;
            this.walletManager = walletManager;
            this.votingManager = votingManager;
            this.settings = poAMinerSettings;
            this.asyncProvider = asyncProvider;
            this.idleFederationMembersKicker = idleFederationMembersKicker;

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.cancellation = CancellationTokenSource.CreateLinkedTokenSource(new[] { nodeLifetime.ApplicationStopping });
            this.votingDataEncoder = new VotingDataEncoder(loggerFactory);

            nodeStats.RegisterStats(this.AddComponentStats, StatsType.Component, this.GetType().Name);
        }

        /// <inheritdoc />
        public virtual void InitializeMining()
        {
            if (this.miningTask == null)
            {
                this.miningTask = this.CreateBlocksAsync();
                this.asyncProvider.RegisterTask($"{nameof(PoAMiner)}.{nameof(this.miningTask)}", this.miningTask);
            }
        }

        private async Task CreateBlocksAsync()
        {
            while (!this.cancellation.IsCancellationRequested)
            {
                try
                {
                    this.logger.LogDebug("IsInitialBlockDownload={0}, AnyConnectedPeers={1}, BootstrappingMode={2}, IsFederationMember={3}",
                        this.ibdState.IsInitialBlockDownload(), this.connectionManager.ConnectedPeers.Any(), this.settings.BootstrappingMode, this.federationManager.IsFederationMember);

                    // Don't mine in IBD or if we aren't connected to any node (unless bootstrapping mode is enabled).
                    // Don't try to mine if we aren't a federation member.
                    bool cantMineAtAll = (this.ibdState.IsInitialBlockDownload() || !this.connectionManager.ConnectedPeers.Any()) && !this.settings.BootstrappingMode;
                    if (cantMineAtAll || !this.federationManager.IsFederationMember)
                    {
                        if (!cantMineAtAll)
                        {
                            string cause = (this.federationManager.CurrentFederationKey == null) ?
                                $"missing file '{KeyTool.KeyFileDefaultName}'" :
                                $"the key in '{KeyTool.KeyFileDefaultName}' not being a federation member";

                            var builder1 = new StringBuilder();
                            builder1.AppendLine("<<==============================================================>>");
                            builder1.AppendLine($"Can't mine due to {cause}.");
                            builder1.AppendLine("<<==============================================================>>");
                            this.logger.LogInformation(builder1.ToString());
                        }

                        int attemptDelayMs = 30_000;
                        await Task.Delay(attemptDelayMs, this.cancellation.Token).ConfigureAwait(false);

                        continue;
                    }

                    uint miningTimestamp = await this.WaitUntilMiningSlotAsync().ConfigureAwait(false);

                    ChainedHeader chainedHeader = await this.MineBlockAtTimestampAsync(miningTimestamp).ConfigureAwait(false);

                    if (chainedHeader == null)
                    {
                        int attemptDelayMs = 500;
                        await Task.Delay(attemptDelayMs, this.cancellation.Token).ConfigureAwait(false);

                        continue;
                    }

                    var builder = new StringBuilder();
                    builder.AppendLine("<<==============================================================>>");
                    builder.AppendLine($"Block mined hash   : '{chainedHeader}'");
                    builder.AppendLine($"Block miner pubkey : '{this.federationManager.CurrentFederationKey.PubKey.ToString()}'");
                    builder.AppendLine("<<==============================================================>>");
                    this.logger.LogInformation(builder.ToString());

                    // The purpose of bootstrap mode is to kickstart the network when the last mined block is very old, which would normally put the node in IBD and inhibit mining.
                    // There is therefore no point keeping this mode enabled once this node has mined successfully.
                    // Additionally, keeping it enabled may result in network splits if this node becomes disconnected from its peers for a prolonged period.
                    if (this.settings.BootstrappingMode)
                    {
                        this.logger.LogInformation("Disabling bootstrap mode as a block has been successfully mined.");

                        this.settings.DisableBootstrap();
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (ConsensusErrorException ce)
                {
                    // Text from PosMinting:
                    // All consensus exceptions should be ignored. It means that the miner
                    // ran into problems while constructing block or verifying it
                    // but it should not halt the mining operation.
                    this.logger.LogWarning("Miner failed to mine block due to: '{0}'.", ce.ConsensusError.Message);
                }
                catch (ConsensusException ce)
                {
                    // Text from PosMinting:
                    // All consensus exceptions (including translated ConsensusErrorException) should be ignored. It means that the miner
                    // ran into problems while constructing block or verifying it
                    // but it should not halt the mining operation.
                    this.logger.LogWarning("Miner failed to mine block due to: '{0}'.", ce.Message);
                }
                catch (Exception exception)
                {
                    this.logger.LogCritical("Exception occurred during mining: {0}", exception.ToString());
                    break;
                }
            }
        }

        private async Task<uint> WaitUntilMiningSlotAsync()
        {
            uint? myTimestamp = null;

            while (!this.cancellation.IsCancellationRequested)
            {
                uint timeNow = (uint)this.dateTimeProvider.GetAdjustedTimeAsUnixTimestamp();

                if (timeNow <= this.consensusManager.Tip.Header.Time)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                    continue;
                }

                if (myTimestamp == null)
                {
                    try
                    {
                        myTimestamp = this.slotsManager.GetMiningTimestamp(timeNow);
                    }
                    catch (NotAFederationMemberException)
                    {
                        this.logger.LogWarning("This node is no longer a federation member!");

                        throw new OperationCanceledException();
                    }
                }

                int estimatedWaitingTime = (int)(myTimestamp - timeNow) - 1;

                if (estimatedWaitingTime <= 0)
                    return myTimestamp.Value;

                await Task.Delay(TimeSpan.FromMilliseconds(500), this.cancellation.Token).ConfigureAwait(false);
            }

            throw new OperationCanceledException();
        }

        protected async Task<ChainedHeader> MineBlockAtTimestampAsync(uint timestamp)
        {
            ChainedHeader tip = this.consensusManager.Tip;

            // Timestamp should always be greater than prev one.
            if (timestamp <= tip.Header.Time)
            {
                // Can happen only when target spacing had crazy low value or key was compromised and someone is mining with our key.
                this.logger.LogWarning("Somehow another block was connected with greater timestamp. Dropping current block.");
                this.logger.LogTrace("(-)[ANOTHER_BLOCK_CONNECTED]:null");
                return null;
            }

            // If an address is specified for mining then preferentially use that.
            // The private key for this address is not used for block signing, so it can be any valid address.
            // Since it is known which miner mines in each block already it does not change the privacy level that every block mines to the same address.
            if (!string.IsNullOrWhiteSpace(this.settings.MineAddress))
            {
                this.walletScriptPubKey = BitcoinAddress.Create(this.settings.MineAddress, this.network).ScriptPubKey;
            }
            else
            {
                // Get the first address from the wallet. In a network with an account-based model the mined UTXOs should all be sent to a predictable address.
                if (this.walletScriptPubKey == null || this.walletScriptPubKey == Script.Empty)
                {
                    this.walletScriptPubKey = this.GetScriptPubKeyFromWallet();

                    // The node could not have a wallet, or the first account/address could have been incorrectly created.
                    if (this.walletScriptPubKey == null)
                    {
                        this.logger.LogWarning("The miner wasn't able to get an address from the wallet, you will not receive any rewards (if no wallet exists, please create one).");
                        this.walletScriptPubKey = new Script();
                    }
                }
            }

            BlockTemplate blockTemplate = this.blockDefinition.Build(tip, this.walletScriptPubKey);

            this.FillBlockTemplate(blockTemplate, out bool dropTemplate);

            if (dropTemplate)
            {
                this.logger.LogTrace("(-)[DROPPED]:null");
                return null;
            }

            blockTemplate.Block.Header.Time = timestamp;

            // Update merkle root.
            blockTemplate.Block.UpdateMerkleRoot();

            // Sign block with our private key.
            var header = blockTemplate.Block.Header as PoABlockHeader;
            this.poaHeaderValidator.Sign(this.federationManager.CurrentFederationKey, header);

            ChainedHeader chainedHeader = await this.consensusManager.BlockMinedAsync(blockTemplate.Block).ConfigureAwait(false);

            if (chainedHeader == null)
            {
                // Block wasn't accepted because we already connected block from the network.
                this.logger.LogTrace("(-)[FAILED_TO_CONNECT]:null");
                return null;
            }

            ValidationContext result = this.integrityValidator.VerifyBlockIntegrity(chainedHeader, blockTemplate.Block);
            if (result.Error != null)
            {
                // Sanity check. Should never happen.
                this.logger.LogTrace("(-)[INTEGRITY_FAILURE]");
                throw new Exception(result.Error.ToString());
            }

            return chainedHeader;
        }

        /// <summary>Fills block template with custom non-standard data.</summary>
        protected virtual void FillBlockTemplate(BlockTemplate blockTemplate, out bool dropTemplate)
        {
            if (this.network.ConsensusOptions.VotingEnabled)
            {
                if (this.network.ConsensusOptions.AutoKickIdleMembers)
                {
                    // Determine whether or not any miners should be scheduled to be kicked from the federation at the current tip.
                    this.idleFederationMembersKicker.Execute(this.consensusManager.Tip);
                }

                // Add scheduled voting data to the block.
                this.AddVotingData(blockTemplate);
            }

            dropTemplate = false;
        }

        /// <summary>Gets scriptPubKey from the wallet.</summary>
        private Script GetScriptPubKeyFromWallet()
        {
            string walletName = this.walletManager.GetWalletsNames().FirstOrDefault();

            if (walletName == null)
                return null;

            HdAccount account = this.walletManager.GetAccounts(walletName).FirstOrDefault();

            if (account == null)
                return null;

            HdAddress address = account.ExternalAddresses.FirstOrDefault();

            return address?.Pubkey;
        }

        /// <summary>Adds OP_RETURN output to a coinbase transaction which contains encoded voting data.</summary>
        /// <remarks>If there are no votes scheduled output will not be added.</remarks>
        private void AddVotingData(BlockTemplate blockTemplate)
        {
            List<VotingData> scheduledVotes = this.votingManager.GetAndCleanScheduledVotes();

            if (scheduledVotes.Count == 0)
            {
                this.logger.LogTrace("(-)[NO_DATA]");
                return;
            }

            var votingData = new List<byte>(VotingDataEncoder.VotingOutputPrefixBytes);

            byte[] encodedVotingData = this.votingDataEncoder.Encode(scheduledVotes);
            votingData.AddRange(encodedVotingData);

            var votingOutputScript = new Script(OpcodeType.OP_RETURN, Op.GetPushOp(votingData.ToArray()));

            blockTemplate.Block.Transactions[0].AddOutput(Money.Zero, votingOutputScript);
        }

        [NoTrace]
        private void AddComponentStats(StringBuilder log)
        {
            log.AppendLine(">> Miner");

            if (this.ibdState.IsInitialBlockDownload())
            {
                log.AppendLine("Mining information is not available whilst the node is syncing.");
                log.AppendLine("The node will mine once it reaches the network's height.");
                log.AppendLine();
                return;
            }

            ChainedHeader tip = this.consensusManager.Tip;
            ChainedHeader currentHeader = tip;

            int pubKeyTakeCharacters = 5;
            int hitCount = 0;

            List<IFederationMember> modifiedFederation = this.votingManager?.GetModifiedFederation(currentHeader) ?? this.federationManager.GetFederationMembers();

            int maxDepth = modifiedFederation.Count;

            log.AppendLine($"Mining information for the last { maxDepth } blocks.");
            log.AppendLine("Note that '<' and '>' surrounds a slot where a miner didn't produce a block.");

            uint timeHeader = (uint)this.dateTimeProvider.GetAdjustedTimeAsUnixTimestamp();
            timeHeader -= timeHeader % this.network.ConsensusOptions.TargetSpacingSeconds;
            if (timeHeader < currentHeader.Header.Time)
                timeHeader += this.network.ConsensusOptions.TargetSpacingSeconds;

            // Iterate mining slots.
            for (int i = 0; i < maxDepth; i++)
            {
                int headerSlot = (int)(timeHeader / this.network.ConsensusOptions.TargetSpacingSeconds) % modifiedFederation.Count;

                PubKey pubKey = modifiedFederation[headerSlot].PubKey;

                string pubKeyRepresentation = (pubKey == this.federationManager.CurrentFederationKey?.PubKey) ? "█████" : pubKey.ToString().Substring(0, pubKeyTakeCharacters);

                // Mined in this slot?
                if (timeHeader == currentHeader.Header.Time)
                {
                    log.Append($"[{ pubKeyRepresentation }] ");

                    currentHeader = currentHeader.Previous;
                    hitCount++;

                    modifiedFederation = this.votingManager?.GetModifiedFederation(currentHeader) ?? this.federationManager.GetFederationMembers();
                }
                else
                {
                    log.Append($"<{ pubKeyRepresentation }> ");
                }

                timeHeader -= this.network.ConsensusOptions.TargetSpacingSeconds;

                if ((i % 20) == 19)
                    log.AppendLine();
            }

            log.Append("...");
            log.AppendLine();
            log.AppendLine($"Miner hits".PadRight(LoggingConfiguration.ColumnLength) + $": {hitCount} of {maxDepth}({(((float)hitCount / (float)maxDepth)).ToString("P2")})");
            log.AppendLine($"Miner idle time".PadRight(LoggingConfiguration.ColumnLength) + $": { TimeSpan.FromSeconds(this.network.ConsensusOptions.TargetSpacingSeconds * (maxDepth - hitCount)).ToString(@"hh\:mm\:ss")}");
            log.AppendLine();
        }

        /// <inheritdoc/>
        public virtual void Dispose()
        {
            this.cancellation.Cancel();
            this.miningTask?.GetAwaiter().GetResult();

            this.cancellation.Dispose();
        }
    }
}
