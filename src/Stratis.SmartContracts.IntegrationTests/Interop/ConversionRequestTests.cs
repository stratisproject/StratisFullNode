using Stratis.Bitcoin.Features.FederatedPeg;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Utilities;
using Stratis.SmartContracts.Networks;
using Xunit;

namespace Stratis.SmartContracts.IntegrationTests
{
    public class ConversionRequestTests
    {
        [Fact]
        public void CanSaveConversionRequest()
        {
            var network = new SmartContractsPoARegTest();
            var dataFolder = TestBase.CreateDataFolder(this, network: network);
            
            var serializer = new DBreezeSerializer(network.Consensus.ConsensusFactory);
            var kvs = new ConversionRequestKeyValueStore(dataFolder, serializer);

            var repo = new ConversionRequestRepository(kvs);

            var request = new ConversionRequest()
            {
                RequestId = "requestId",
                RequestType = (int)ConversionRequestType.Mint,
                Processed = false,
                RequestStatus = (int)ConversionRequestStatus.Unprocessed,
                Amount = 100000000,
                BlockHeight = 123,
                DestinationAddress = ""
            };

            repo.Save(request);
        }

        [Fact]
        public void CanRetrieveSavedConversionRequest()
        {
            var network = new SmartContractsPoARegTest();
            var dataFolder = TestBase.CreateDataFolder(this, network: network);

            var serializer = new DBreezeSerializer(network.Consensus.ConsensusFactory);
            var kvs = new ConversionRequestKeyValueStore(dataFolder, serializer);

            var repo = new ConversionRequestRepository(kvs);

            var request = new ConversionRequest()
            {
                RequestId = "requestId",
                RequestType = (int)ConversionRequestType.Mint,
                Processed = false,
                RequestStatus = (int)ConversionRequestStatus.Unprocessed,
                Amount = 100000000,
                BlockHeight = 123,
                DestinationAddress = ""
            };

            repo.Save(request);

            var request2 = repo.Get(request.RequestId);

            Assert.Equal(request.RequestId, request2.RequestId);
            Assert.Equal(request.RequestType, request2.RequestType);
            Assert.Equal(request.Processed, request2.Processed);
            Assert.Equal(request.RequestStatus, request2.RequestStatus);
            Assert.Equal(request.Amount, request2.Amount);
            Assert.Equal(request.BlockHeight, request2.BlockHeight);
            Assert.Equal(request.DestinationAddress, request2.DestinationAddress);
        }

        [Fact]
        public void RetrievingInvalidRequestIdFails()
        {
            var network = new SmartContractsPoARegTest();
            var dataFolder = TestBase.CreateDataFolder(this, network: network);

            var serializer = new DBreezeSerializer(network.Consensus.ConsensusFactory);
            var kvs = new ConversionRequestKeyValueStore(dataFolder, serializer);

            var repo = new ConversionRequestRepository(kvs);

            var request = repo.Get("nonexistent");

            Assert.Null(request);
        }
    }
}
