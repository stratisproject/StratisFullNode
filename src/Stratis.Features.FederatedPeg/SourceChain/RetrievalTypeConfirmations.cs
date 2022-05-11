using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.TargetChain;
using Stratis.Features.PoA.Collateral.CounterChain;

namespace Stratis.Features.FederatedPeg.SourceChain
{
    public interface IRetrievalTypeConfirmations
    {
        int GetDepositConfirmations(int depositHeight, DepositRetrievalType retrievalType);

        int GetDepositMaturityHeight(int depositHeight, DepositRetrievalType retrievalType);

        /// <summary>
        /// Determines the maximum distance to look back to find deposits that would be maturing at the specified height.
        /// </summary>
        /// <param name="maturityHeight">The minimum height at which deposits would be maturing.</param>
        /// <returns>The maximum distance to look back to find deposits that would be maturing at the specified height.</returns>
        int MaximumConfirmationsAtMaturityHeight(int maturityHeight);

        DepositRetrievalType[] GetRetrievalTypes();
    }

    public class RetrievalTypeConfirmations : IRetrievalTypeConfirmations
    {
        private const int LowConfirmations = 10;         //  450 seconds - 7m30s
        private const int MediumConfirmations = 25;      // 1125 seconds - 18m45s
        private const int HighConfirmations = 50;        // 2250 seconds - 37m30s
        private const int CirrusLowConfirmations = 30;   //  480 seconds - 8m0s
        private const int CirrusMediumConfirmation = 70; // 1120 seconds - 18m40s
        private const int CirrusHighConfirmations = 140; // 2240 seconds - 37m20s

        private readonly NodeDeployments nodeDeployments;
        private readonly Dictionary<DepositRetrievalType, int> legacyRetrievalTypeConfirmations;
        private readonly Dictionary<DepositRetrievalType, int> retrievalTypeConfirmations;
        private readonly Network network;
        private readonly IMaturedBlocksSyncManager maturedBlocksSyncManager;
        private readonly ICounterChainSettings counterChainSettings;

        public RetrievalTypeConfirmations(Network network, NodeDeployments nodeDeployments, IFederatedPegSettings federatedPegSettings, IMaturedBlocksSyncManager maturedBlocksSyncManager, ICounterChainSettings counterChainSettings)
        {
            this.nodeDeployments = nodeDeployments;
            this.network = network;
            this.maturedBlocksSyncManager = maturedBlocksSyncManager;
            this.counterChainSettings = counterChainSettings;
            this.legacyRetrievalTypeConfirmations = new Dictionary<DepositRetrievalType, int>
            {
                [DepositRetrievalType.Small] = federatedPegSettings.MinimumConfirmationsSmallDeposits,
                [DepositRetrievalType.Normal] = federatedPegSettings.MinimumConfirmationsNormalDeposits,
                [DepositRetrievalType.Large] = federatedPegSettings.MinimumConfirmationsLargeDeposits
            };

            if (federatedPegSettings.IsMainChain)
            {
                this.legacyRetrievalTypeConfirmations[DepositRetrievalType.Distribution] = federatedPegSettings.MinimumConfirmationsDistributionDeposits;
                this.legacyRetrievalTypeConfirmations[DepositRetrievalType.ConversionSmall] = federatedPegSettings.MinimumConfirmationsSmallDeposits;
                this.legacyRetrievalTypeConfirmations[DepositRetrievalType.ConversionNormal] = federatedPegSettings.MinimumConfirmationsNormalDeposits;
                this.legacyRetrievalTypeConfirmations[DepositRetrievalType.ConversionLarge] = federatedPegSettings.MinimumConfirmationsLargeDeposits;
            }

            if (this.network.Name.StartsWith("Cirrus"))
            {
                this.retrievalTypeConfirmations = new Dictionary<DepositRetrievalType, int>
                {
                    [DepositRetrievalType.Small] = CirrusLowConfirmations,
                    [DepositRetrievalType.Normal] = CirrusMediumConfirmation,
                    [DepositRetrievalType.Large] = CirrusHighConfirmations
                };

                if (federatedPegSettings.IsMainChain)
                {
                    this.retrievalTypeConfirmations[DepositRetrievalType.Distribution] = CirrusHighConfirmations;
                    this.retrievalTypeConfirmations[DepositRetrievalType.ConversionSmall] = CirrusLowConfirmations;
                    this.retrievalTypeConfirmations[DepositRetrievalType.ConversionNormal] = CirrusMediumConfirmation;
                    this.retrievalTypeConfirmations[DepositRetrievalType.ConversionLarge] = CirrusHighConfirmations;
                }
            }
            else
            {
                this.retrievalTypeConfirmations = new Dictionary<DepositRetrievalType, int>()
                {
                    [DepositRetrievalType.Small] = LowConfirmations,
                    [DepositRetrievalType.Normal] = MediumConfirmations,
                    [DepositRetrievalType.Large] = HighConfirmations
                };

                if (federatedPegSettings.IsMainChain)
                {
                    this.retrievalTypeConfirmations[DepositRetrievalType.Distribution] = HighConfirmations;
                    this.retrievalTypeConfirmations[DepositRetrievalType.ConversionSmall] = LowConfirmations;
                    this.retrievalTypeConfirmations[DepositRetrievalType.ConversionNormal] = MediumConfirmations;
                    this.retrievalTypeConfirmations[DepositRetrievalType.ConversionLarge] = HighConfirmations;
                }
            }
        }

        public int MaximumConfirmationsAtMaturityHeight(int maturityHeight)
        {
            if (maturityHeight < this.Release1300ActivationHeight)
                return this.legacyRetrievalTypeConfirmations.Values.Max();

            return this.retrievalTypeConfirmations.Values.Max();
        }

        private int Release1300ActivationHeight
        {
            get
            {
                if (this.network.Name.StartsWith("Cirrus"))
                    return (this.nodeDeployments?.BIP9.ArraySize > 0) ? this.nodeDeployments.BIP9.ActivationHeightProviders[0 /* Release 1300 */].ActivationHeight : 0;

                // This code is running on the main chain.
                if (this.counterChainSettings.CounterChainNetwork.Consensus.BIP9Deployments.Length == 0)
                    return 0;

                return this.maturedBlocksSyncManager.GetMainChainActivationHeight();
            }
        }

        public int GetDepositConfirmations(int depositHeight, DepositRetrievalType retrievalType)
        {
            // Keep everything maturity-height-centric. Otherwise the way we use MaximumConfirmationsAtMaturityHeight will have to change as well.
            if (depositHeight + this.legacyRetrievalTypeConfirmations[retrievalType] < this.Release1300ActivationHeight)
                return this.legacyRetrievalTypeConfirmations[retrievalType];

            return this.retrievalTypeConfirmations[retrievalType];
        }

        public int GetDepositMaturityHeight(int depositHeight, DepositRetrievalType retrievalType)
        {
            return depositHeight + GetDepositConfirmations(depositHeight, retrievalType);
        }

        public DepositRetrievalType[] GetRetrievalTypes()
        {
            return this.retrievalTypeConfirmations.Keys.ToArray();
        }
    }
}
