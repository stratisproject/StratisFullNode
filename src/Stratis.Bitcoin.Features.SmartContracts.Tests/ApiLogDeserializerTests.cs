using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Moq;
using NBitcoin;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Caching;
using Stratis.SmartContracts.CLR.Loader;
using Stratis.SmartContracts.CLR.Serialization;
using Stratis.SmartContracts.Core.Receipts;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.Core.State.AccountAbstractionLayer;
using Stratis.SmartContracts.Networks;
using Xunit;

namespace Stratis.Bitcoin.Features.SmartContracts.Tests
{
    public class ApiLogDeserializerTests
    {
        public struct TestLog
        {
            public uint Id;
            public string Name;
            public byte Data;
            public byte[] Datas;
            public bool Truth;
            public Address Address;
            public UInt128 Value128;
            public UInt256 Value256;
        }

        [Fact]
        public void Deserialize_Basic_Log_Success()
        {
            var network = new SmartContractsRegTest();
            var primitiveSerializer = new ContractPrimitiveSerializerV2(network);

            var testStruct = new TestLog
            {
                Id = uint.MaxValue,
                Name = "Test ID",
                Data = 0xAA,
                Datas = new byte[] { 0xBB, 0xCC, 0xDD },
                Truth = true,
                Address = "0x0000000000000000000000000000000000000001".HexToAddress(),
                Value128 = 123,
                Value256 = 456
            };

            var testBytes = primitiveSerializer.Serialize(testStruct);

            var serializer = new ApiLogDeserializer(primitiveSerializer, network, Mock.Of<IStateRepositoryRoot>(), Mock.Of<IContractAssemblyCache>());
            dynamic deserializedLog = serializer.DeserializeLogData(testBytes, typeof(TestLog));

            Assert.Equal(testStruct.Id, deserializedLog.Data.Id);
            Assert.Equal(testStruct.Name, deserializedLog.Data.Name);
            Assert.Equal(testStruct.Data, deserializedLog.Data.Data);
            Assert.True(testStruct.Datas.SequenceEqual((byte[])deserializedLog.Data.Datas));
            Assert.Equal(testStruct.Truth, deserializedLog.Data.Truth);
            Assert.Equal(testStruct.Address.ToUint160().ToBase58Address(network), deserializedLog.Data.Address);
            Assert.Equal(testStruct.Value128.ToString(), deserializedLog.Data.Value128.ToString());
            Assert.Equal(testStruct.Value256.ToString(), deserializedLog.Data.Value256.ToString());
        }

        [Fact]
        public void Deserialize_Logs_With_Different_Addresses_From_Cache()
        {
            var network = new SmartContractsRegTest();
            var primitiveSerializer = new ContractPrimitiveSerializerV2(network);

            var testStruct0 = new TestLog
            {
                Name = "Test",
                Value128 = 123,
                Value256 = 456
            };

            var testStruct1 = new TestLog
            {
                Name = "Test 2",
                Value128 = 789,
                Value256 = 101112
            };

            var testBytes = primitiveSerializer.Serialize(testStruct0);

            var logs = new Log[]
            {
                new Log(uint160.Zero, new List<byte[]> { Encoding.UTF8.GetBytes("TestLog") }, primitiveSerializer.Serialize(testStruct0)),
                new Log(uint160.One, new List<byte[]> { Encoding.UTF8.GetBytes("TestLog") }, primitiveSerializer.Serialize(testStruct1)),
            };

            var stateRoot = new Mock<IStateRepositoryRoot>();
            stateRoot.Setup(r => r.GetCodeHash(It.IsAny<uint160>())).Returns(uint256.Zero.ToBytes());

            var assemblyCache = new Mock<IContractAssemblyCache>();
            var contractAssembly = new Mock<IContractAssembly>();

            // Return this assembly as it will contain the TestLog type.
            contractAssembly.Setup(s => s.Assembly).Returns(Assembly.GetExecutingAssembly());
            assemblyCache.Setup(s => s.Retrieve(It.IsAny<uint256>())).Returns(new CachedAssemblyPackage(contractAssembly.Object));

            var serializer = new ApiLogDeserializer(primitiveSerializer, network, stateRoot.Object, assemblyCache.Object);

            List<SmartContracts.Models.LogResponse> responses = serializer.MapLogResponses(logs);

            // Verify that we deserialized the logs correctly.
            Assert.Equal(testStruct0.Name, ((dynamic)responses[0].Log.Data).Name);
            Assert.Equal(testStruct1.Name, ((dynamic)responses[1].Log.Data).Name);

            // Verify that we got the code for both log assemblies.
            stateRoot.Verify(s => s.GetCodeHash(logs[0].Address), Times.Once);
            stateRoot.Verify(s => s.GetCodeHash(logs[1].Address), Times.Once);
        }

        [Fact]
        public void MapTransferInfo_Success()
        {
            var network = new SmartContractsRegTest();
            var primitiveSerializer = new ContractPrimitiveSerializerV2(network);

            var stateRoot = new Mock<IStateRepositoryRoot>();
            stateRoot.Setup(r => r.GetCodeHash(It.IsAny<uint160>())).Returns(uint256.Zero.ToBytes());

            var assemblyCache = new Mock<IContractAssemblyCache>();
            var contractAssembly = new Mock<IContractAssembly>();

            var serializer = new ApiLogDeserializer(primitiveSerializer, network, stateRoot.Object, assemblyCache.Object);

            var transferInfos = new List<TransferInfo>
            {
                new TransferInfo(uint160.Zero, uint160.One, 12345),
                new TransferInfo(uint160.One, uint160.Zero, 12345),
            };

            List<SmartContracts.Models.TransferResponse> result = serializer.MapTransferInfo(transferInfos.ToArray());

            for (var i = 0; i < transferInfos.Count; i++)
            {
                Assert.Equal(transferInfos[i].From.ToBase58Address(network), result[i].From);
                Assert.Equal(transferInfos[i].To.ToBase58Address(network), result[i].To);
                Assert.Equal(transferInfos[i].Value, result[i].Value);
            }
        }
    }
}
