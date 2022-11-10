using Stratis.Bitcoin.Features.Interop.Settings;

namespace Stratis.Bitcoin.Features.Interop.ETHClient
{
    public interface IBNBClient : IETHClient
    {
    }

    public class BNBClient : ETHClient, IBNBClient
    {
        public BNBClient(InteropSettings interopSettings) : base(interopSettings)
        {
        }

        protected override void SetupConfiguration(InteropSettings interopSettings)
        {
            this.settings = interopSettings.BNBSettings;
        }
    }
}
