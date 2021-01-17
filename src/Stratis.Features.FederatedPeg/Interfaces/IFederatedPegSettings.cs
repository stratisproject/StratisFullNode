using System.Collections.Generic;
using System.Net;
using NBitcoin;

namespace Stratis.Features.FederatedPeg.Interfaces
{
    /// <summary>
    /// Configuration settings used to initialize a FederationGateway.
    /// </summary>
    public interface IFederatedPegSettings
    {
        /// <summary>
        /// Indicates whether this is the main chain. Set if the "-mainchain" switch was used.
        /// </summary>
        bool IsMainChain { get; }

        /// <summary>
        /// Ip Endpoints for the other nodes in the federation. Useful for connecting to nodes but
        /// not accurate for checking whether a node is a federation member.
        /// </summary>
        HashSet<IPEndPoint> FederationNodeIpEndPoints { get; }

        /// <summary>
        /// Ip addresses for the other nodes in the federation. Useful for checking whether a node is
        /// a federation member.
        /// </summary>
        HashSet<IPAddress> FederationNodeIpAddresses { get; }

        /// <summary>
        /// Public keys of other federation members.
        /// </summary>
        PubKey[] FederationPublicKeys { get; }

        /// <summary>
        /// Public key of this federation member.
        /// </summary>
        string PublicKey { get; }

        /// <summary>
        /// The block number to start syncing the federation wallet from.
        /// </summary>
        int WalletSyncFromHeight { get; }

        /// <summary>
        /// For the M of N multisig, this is the number of signers required to reach a quorum.
        /// </summary>
        int MultiSigM { get; }

        /// <summary>
        /// For the M of N multisig, this is the number of members in the federation.
        /// </summary>
        int MultiSigN { get; }

        /// <summary>
        /// The transaction fee required for withdrawals.
        /// </summary>
        Money GetWithdrawalTransactionFee(int numInputs);

        /// <summary>
        /// The block number on the other chain to start retrieving deposits from.
        /// </summary>
        int CounterChainDepositStartBlock { get; }

        /// <summary> The maximum amount of partial transactions that can be processed in the <see cref="ICrossChainTransferStore"/>.</summary>
        int MaximumPartialTransactionThreshold { get; }

        /// <summary>
        /// Similar to <see cref="MinimumConfirmationsNormalDeposits"/> and <see cref="MinimumConfirmationsLargeDeposits"/> but this settings allows us to specify 
        /// how mature small deposits needs to be in order to be considered for a cross chain transfer.
        /// <para>
        /// Transactions less than or equal to an amont of <see cref="SmallDepositThresholdAmount"/> will be processed.
        /// </para>
        /// </summary>
        int MinimumConfirmationsSmallDeposits { get; }

        /// <summary>
        /// The amount of blocks under which multisig deposit transactions need to be buried before the cross chains transfer actually trigger.
        /// <para>
        /// Transactions with a value of more than <see cref="NormalDepositThresholdAmount"/> and less than or equal to an amont of <see cref="LargeDepositThresholdAmount"/> will be processed.
        /// </para>
        /// </summary>
        int MinimumConfirmationsNormalDeposits { get; }

        /// <summary>
        /// The amount of blocks under which multisig deposit transactions need to be buried before the cross chains transfer actually trigger.
        /// <para>
        /// Transactions with a value of more than <see cref="NormalDepositThresholdAmount"/> will be processed.
        /// </para>
        /// </summary>
        int MinimumConfirmationsLargeDeposits { get; }

        int MinimumConfirmationsDistributionDeposits { get; }

        int MinimumConfirmationsConversionDeposits { get; }

        /// <summary>
        /// Deposits under or equal to this value will be processed after <see cref="MinimumConfirmationsSmallDeposits"/> blocks on the counter-chain.
        /// </summary>
        Money SmallDepositThresholdAmount { get; }

        /// <summary>
        /// Deposits under or equal to this value will be processed after <see cref="MinimumConfirmationsNormalDeposits"/> blocks on the counter-chain.
        /// </summary>
        Money NormalDepositThresholdAmount { get; }

        /// <summary>
        /// Address for the MultiSig script.
        /// </summary>
        BitcoinAddress MultiSigAddress { get; }

        /// <summary>
        /// Pay2Multisig redeem script.
        /// </summary>
        Script MultiSigRedeemScript { get; }
    }
}
