using NBitcoin;

namespace Stratis.Bitcoin.EventBus.CoreEvents
{
    public sealed class FederationWalletStatusEvent : EventBase
    {
        public Money ConfirmedBalance { get; private set; }

        public Money UnconfirmedBalance { get; private set; }

        public FederationWalletStatusEvent(Money confirmedBalance, Money unconfirmedBalance)
        {
            this.ConfirmedBalance = confirmedBalance;
            this.UnconfirmedBalance = unconfirmedBalance;
        }
    }
}
