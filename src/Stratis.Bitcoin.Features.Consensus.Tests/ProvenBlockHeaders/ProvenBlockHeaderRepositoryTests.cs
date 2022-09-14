using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NBitcoin;
using Stratis.Bitcoin.Database;
using Stratis.Bitcoin.Features.Consensus.ProvenBlockHeaders;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Tests.Common.Logging;
using Stratis.Bitcoin.Utilities;
using Xunit;

namespace Stratis.Bitcoin.Features.Consensus.Tests.ProvenBlockHeaders
{
    public class ProvenBlockHeaderRepositoryTests : LogsTestBase
    {
        private readonly DBreezeSerializer dBreezeSerializer;
        private static readonly byte ProvenBlockHeaderTable = 1;
        private static readonly byte BlockHashHeightTable = 2;

        public ProvenBlockHeaderRepositoryTests() : base(KnownNetworks.StraxTest)
        {
            this.dBreezeSerializer = new DBreezeSerializer(this.Network.Consensus.ConsensusFactory);
        }

        [Fact]
        public void Initializes_Genesis_ProvenBlockHeader_OnLoadAsync()
        {
            string folder = CreateTestDir(this);

            // Initialise the repository - this will set-up the genesis blockHash (blockId).
            using (IProvenBlockHeaderRepository repository = this.SetupRepository(this.Network, folder))
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

            using (IProvenBlockHeaderRepository repo = this.SetupRepository(this.Network, folder))
            {
                await repo.PutAsync(items, blockHashHeightPair);
            }

            using (var db = new LevelDb())
            {
                db.Open(folder);

                var headerOut = this.dBreezeSerializer.Deserialize<ProvenBlockHeader>(db.Get(ProvenBlockHeaderTable, BitConverter.GetBytes(blockHashHeightPair.Height)));
                var hashHeightPairOut = this.DBreezeSerializer.Deserialize<HashHeightPair>(db.Get(BlockHashHeightTable, new byte[] { 1 }));

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
            using (IProvenBlockHeaderRepository repo = this.SetupRepository(this.Network, folder))
            {
                await repo.PutAsync(items, new HashHeightPair(header2.GetHash(), items.Count - 1));
            }

            // Check the ProvenBlockHeader exists in the database.
            using (IDb db = new LevelDb())
            {
                db.Open(folder);

                using (IDbIterator iterator = db.GetIterator(ProvenBlockHeaderTable))
                {
                    var headersOut = iterator.GetAll().ToDictionary(i => i.key, i => i.value);

                    headersOut.Keys.Count.Should().Be(2);
                    this.dBreezeSerializer.Deserialize<ProvenBlockHeader>(headersOut.First().Value).GetHash().Should().Be(items[0].GetHash());
                    this.dBreezeSerializer.Deserialize<ProvenBlockHeader>(headersOut.Last().Value).GetHash().Should().Be(items[1].GetHash());
                }
            }
        }

        [Fact]
        public async Task GetAsync_ReadsProvenBlockHeaderAsync()
        {
            string folder = CreateTestDir(this);

            ProvenBlockHeader headerIn = CreateNewProvenBlockHeaderMock();

            int blockHeight = 1;

            using (IDb db = new LevelDb())
            {
                db.Open(folder);

                using (var batch = db.GetWriteBatch())
                {
                    batch.Put(ProvenBlockHeaderTable, BitConverter.GetBytes(blockHeight), this.dBreezeSerializer.Serialize(headerIn));
                    batch.Write();
                }
            }

            // Query the repository for the item that was inserted in the above code.
            using (IProvenBlockHeaderRepository repo = this.SetupRepository(this.Network, folder))
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

            using (var engine = new LevelDb())
            {
                engine.Open(folder);

                using (var batch = engine.GetWriteBatch())
                {
                    batch.Put(ProvenBlockHeaderTable, BitConverter.GetBytes(1), this.dBreezeSerializer.Serialize(CreateNewProvenBlockHeaderMock()));
                    batch.Put(BlockHashHeightTable, new byte[0], this.DBreezeSerializer.Serialize(new HashHeightPair(new uint256(), 1)));
                    batch.Write();
                }
            }

            using (IProvenBlockHeaderRepository repo = this.SetupRepository(this.Network, folder))
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
            using (IProvenBlockHeaderRepository repo = this.SetupRepository(this.Network, folder))
            {
                await repo.PutAsync(headers, new HashHeightPair(headers.Last().Value.GetHash(), headers.Count - 1));
            }

            using (IProvenBlockHeaderRepository newRepo = this.SetupRepository(this.Network, folder))
            {
                newRepo.TipHashHeight.Hash.Should().Be(headers.Last().Value.GetHash());
                newRepo.TipHashHeight.Height.Should().Be(headers.Count - 1);
            }
        }

        private IProvenBlockHeaderRepository SetupRepository(Network network, string folder)
        {
            var repo = new ProvenBlockHeaderRepository<LevelDb>(network, folder, this.LoggerFactory.Object, this.dBreezeSerializer);

            Task task = repo.InitializeAsync();

            task.Wait();

            return repo;
        }
    }
}