using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NBitcoin;
using RocksDbSharp;
using Stratis.Bitcoin.Features.Consensus.ProvenBlockHeaders;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Persistence;
using Stratis.Bitcoin.Tests.Common.Logging;
using Stratis.Bitcoin.Utilities;
using Xunit;

namespace Stratis.Bitcoin.Features.Consensus.Tests.ProvenBlockHeaders
{
    public sealed class RocksDbProvenBlockHeaderRepositoryTests : LogsTestBase
    {
        private readonly DBreezeSerializer dBreezeSerializer;
        private static readonly byte ProvenBlockHeaderTable = 1;
        private static readonly byte BlockHashHeightTable = 2;

        public RocksDbProvenBlockHeaderRepositoryTests() : base(new StraxTest())
        {
            this.dBreezeSerializer = new DBreezeSerializer(this.Network.Consensus.ConsensusFactory);
        }

        [Fact]
        public void Initializes_Genesis_ProvenBlockHeader_OnLoadAsync()
        {
            string folder = CreateTestDir(this);

            // Initialise the repository - this will set-up the genesis blockHash (blockId).
            using (IProvenBlockHeaderRepository repository = this.SetupRepository(folder))
            {
                // Check the BlockHash (blockId) exists.
                repository.TipHashHeight.Height.Should().Be(0);
                repository.TipHashHeight.Hash.Should().Be(this.Network.GetGenesis().GetHash());
            }
        }

        [Fact]
        public async Task PutAsync_WritesProvenBlockHeaderAndSavesBlockHashAsync()
        {
            string folder = CreateTestDir(this);

            ProvenBlockHeader provenBlockHeaderIn = CreateNewProvenBlockHeaderMock();

            var blockHashHeightPair = new HashHeightPair(provenBlockHeaderIn.GetHash(), 0);
            var items = new SortedDictionary<int, ProvenBlockHeader>() { { 0, provenBlockHeaderIn } };

            using (IProvenBlockHeaderRepository repo = this.SetupRepository(folder))
            {
                await repo.PutAsync(items, blockHashHeightPair);
            }

            using (var engine = RocksDb.Open(new DbOptions().SetCreateIfMissing(), folder))
            {
                var headerOut = this.dBreezeSerializer.Deserialize<ProvenBlockHeader>(engine.Get(ProvenBlockHeaderTable, BitConverter.GetBytes(blockHashHeightPair.Height)));
                var hashHeightPairOut = this.DBreezeSerializer.Deserialize<HashHeightPair>(engine.Get(BlockHashHeightTable, new byte[] { 1 }));

                headerOut.Should().NotBeNull();
                headerOut.GetHash().Should().Be(provenBlockHeaderIn.GetHash());

                hashHeightPairOut.Should().NotBeNull();
                hashHeightPairOut.Hash.Should().Be(provenBlockHeaderIn.GetHash());
            }
        }

        [Fact]
        public async Task PutAsync_Inserts_MultipleProvenBlockHeadersAsync()
        {
            string folder = CreateTestDir(this);

            PosBlock posBlock = CreatePosBlock();
            ProvenBlockHeader header1 = CreateNewProvenBlockHeaderMock(posBlock);
            ProvenBlockHeader header2 = CreateNewProvenBlockHeaderMock(posBlock);

            var items = new SortedDictionary<int, ProvenBlockHeader>() { { 0, header1 }, { 1, header2 } };

            // Put the items in the repository.
            using (IProvenBlockHeaderRepository repo = this.SetupRepository(folder))
            {
                await repo.PutAsync(items, new HashHeightPair(header2.GetHash(), items.Count - 1));
            }

            // Check the ProvenBlockHeader exists in the database.
            using (var engine = RocksDb.Open(new DbOptions().SetCreateIfMissing(), folder))
            {
                var headersOut = new Dictionary<byte[], byte[]>();
                var enumerator = engine.NewIterator();
                enumerator.SeekToFirst();
                while (enumerator.Valid())
                {
                    if (enumerator.Key()[0] == ProvenBlockHeaderTable)
                        headersOut.Add(enumerator.Key(), enumerator.Value());

                    enumerator.Next();
                }

                headersOut.Keys.Count.Should().Be(2);
                this.dBreezeSerializer.Deserialize<ProvenBlockHeader>(headersOut.First().Value).GetHash().Should().Be(items[0].GetHash());
                this.dBreezeSerializer.Deserialize<ProvenBlockHeader>(headersOut.Last().Value).GetHash().Should().Be(items[1].GetHash());
            }
        }

        [Fact]
        public async Task GetAsync_ReadsProvenBlockHeaderAsync()
        {
            string folder = CreateTestDir(this);

            ProvenBlockHeader headerIn = CreateNewProvenBlockHeaderMock();

            int blockHeight = 1;

            using (var engine = RocksDb.Open(new DbOptions().SetCreateIfMissing(), folder))
            {
                engine.Put(ProvenBlockHeaderTable, BitConverter.GetBytes(blockHeight), this.dBreezeSerializer.Serialize(headerIn));
            }

            // Query the repository for the item that was inserted in the above code.
            using (var repo = this.SetupRepository(folder))
            {
                var headerOut = await repo.GetAsync(blockHeight).ConfigureAwait(false);

                headerOut.Should().NotBeNull();
                uint256.Parse(headerOut.ToString()).Should().Be(headerOut.GetHash());
            }
        }

        [Fact]
        public async Task GetAsync_WithWrongBlockHeightReturnsNullAsync()
        {
            string folder = CreateTestDir(this);

            using (var engine = RocksDb.Open(new DbOptions().SetCreateIfMissing(), folder))
            {
                engine.Put(ProvenBlockHeaderTable, BitConverter.GetBytes(1), this.dBreezeSerializer.Serialize(CreateNewProvenBlockHeaderMock()));
                engine.Put(BlockHashHeightTable, new byte[0], this.DBreezeSerializer.Serialize(new HashHeightPair(new uint256(), 1)));
            }

            using (var repo = this.SetupRepository(folder))
            {
                // Select a different block height.
                ProvenBlockHeader outHeader = await repo.GetAsync(2).ConfigureAwait(false);
                outHeader.Should().BeNull();

                // Select the original item inserted into the table
                outHeader = await repo.GetAsync(1).ConfigureAwait(false);
                outHeader.Should().NotBeNull();
            }
        }

        [Fact]
        public async Task PutAsync_DisposeOnInitialise_ShouldBeAtLastSavedTipAsync()
        {
            string folder = CreateTestDir(this);

            PosBlock posBlock = CreatePosBlock();
            var headers = new SortedDictionary<int, ProvenBlockHeader>();

            for (int i = 0; i < 10; i++)
            {
                headers.Add(i, CreateNewProvenBlockHeaderMock(posBlock));
            }

            // Put the items in the repository.
            using (IProvenBlockHeaderRepository repo = this.SetupRepository(folder))
            {
                await repo.PutAsync(headers, new HashHeightPair(headers.Last().Value.GetHash(), headers.Count - 1));
            }

            using (IProvenBlockHeaderRepository newRepo = this.SetupRepository(folder))
            {
                newRepo.TipHashHeight.Hash.Should().Be(headers.Last().Value.GetHash());
                newRepo.TipHashHeight.Height.Should().Be(headers.Count - 1);
            }
        }

        private RocksDbProvenBlockHeaderRepository SetupRepository(string folder)
        {
            var repo = new RocksDbProvenBlockHeaderRepository(folder, this.dBreezeSerializer, this.Network);

            Task task = repo.InitializeAsync();

            task.Wait();

            return repo;
        }
    }
}
