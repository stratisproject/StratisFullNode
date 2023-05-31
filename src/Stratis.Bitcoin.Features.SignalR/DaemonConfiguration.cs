﻿using Stratis.Bitcoin.Features.SignalR.Broadcasters;
using Stratis.Bitcoin.Features.SignalR.Events;

namespace Stratis.Bitcoin.Features.SignalR
{
    public static class DaemonConfiguration
    {
        private static readonly IClientEvent[] EventsToHandle = new IClientEvent[]
        {
            new BlockConnectedClientEvent(),
            new ReconstructFederationClientEvent(),
            new FullNodeClientEvent(),
            new TransactionReceivedClientEvent(),
            new WalletProcessedTransactionOfInterestClientEvent(),
            new MultiSigMemberStateRequestClientEvent(),
            new TransactionAddedToMemoryPoolClientEvent(),
            new MiningStatisticsClientEvent(),
            new ConsensusManagerStatusClientEvent(),
            new PeerConnectionInfoClientEvent(),
            new FederationWalletStatusClientEvent(),
            new AddressIndexerStatusClientEvent(),
        };

        private static ClientEventBroadcasterSettings Settings = new ClientEventBroadcasterSettings
        {
            BroadcastFrequencySeconds = 5
        };

        public static void ConfigureSignalRForCirrus(SignalROptions options)
        {
            options.EventsToHandle = EventsToHandle;

            options.ClientEventBroadcasters = new[]
            {
                (Broadcaster: typeof(CirrusWalletInfoBroadcaster), ClientEventBroadcasterSettings: Settings)
            };
        }

        public static void ConfigureSignalRForStrax(SignalROptions options)
        {
            options.EventsToHandle = EventsToHandle;

            options.ClientEventBroadcasters = new[]
            {
                (Broadcaster: typeof(StakingBroadcaster), ClientEventBroadcasterSettings: Settings),
                (Broadcaster: typeof(WalletInfoBroadcaster), ClientEventBroadcasterSettings:Settings)
            };
        }
    }
}
