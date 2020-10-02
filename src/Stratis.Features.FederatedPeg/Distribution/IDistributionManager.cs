namespace Stratis.Features.FederatedPeg.Distribution
{
    public interface IDistributionManager
    {
        void Distribute(DistributionRecord record);
    }
}
