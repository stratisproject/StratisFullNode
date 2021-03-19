using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Flurl;
using Flurl.Http;
using NBitcoin;
using Newtonsoft.Json;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Xunit;

namespace Stratis.Bitcoin.IntegrationTests.Wallet
{
    public class AddressBookTests
    {
        private readonly Network network;

        public AddressBookTests()
        {
            this.network = new StraxRegTest();
        }

        [Fact]
        public async Task AddAnAddressBookEntryAsync()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                // Arrange.
                CoreNode node = builder.CreateStratisPosNode(this.network).Start();

                string address1 = new Key().PubKey.Hash.GetAddress(this.network).ToString();

                // Act.
                AddressBookEntryModel newEntry = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook/address")
                    .PostJsonAsync(new { label = "label1", address = address1 })
                    .ReceiveJson<AddressBookEntryModel>();

                // Assert.
                // Check the address is in the address book.
                AddressBookModel addressBook = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook")
                    .GetJsonAsync<AddressBookModel>();

                addressBook.Addresses.Should().ContainSingle();
                addressBook.Addresses.Single().Label.Should().Be("label1");
                addressBook.Addresses.Single().Address.Should().Be(address1);
            }
        }

        [Fact]
        public async Task AddAnAddressBookEntryWhenAnEntryAlreadyExistsAsync()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                // Arrange.
                CoreNode node = builder.CreateStratisPosNode(this.network).Start();

                string address1 = new Key().PubKey.Hash.GetAddress(this.network).ToString();
                string address2 = new Key().PubKey.Hash.GetAddress(this.network).ToString();

                // Add a first address.
                AddressBookEntryModel newEntry = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook/address")
                    .PostJsonAsync(new { label = "label1", address = address1 })
                    .ReceiveJson<AddressBookEntryModel>();

                // Act.
                // Add an entry with the same address and label already exist.
                Func<Task> firstAttempt = async () => await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook/address")
                    .PostJsonAsync(new { label = "label1", address = address1 })
                    .ReceiveJson<AddressBookEntryModel>();

                // Add an entry with the same address only already exist.
                Func<Task> secondAttempt = async () => await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook/address")
                    .PostJsonAsync(new { label = "label2", address = address1 })
                    .ReceiveJson<AddressBookEntryModel>();

                // Add an entry with the same label already exist.
                Func<Task> thirdAttempt = async () => await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook/address")
                    .PostJsonAsync(new { label = "label1", address = address2 })
                    .ReceiveJson<AddressBookEntryModel>();

                // Assert.
                var exception = firstAttempt.Should().Throw<FlurlHttpException>().Which;
                var response = exception.Call.Response;
                ErrorResponse errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(await response.ResponseMessage.Content.ReadAsStringAsync());
                List<ErrorModel> errors = errorResponse.Errors;
                response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
                errors.Should().ContainSingle();
                errors.First().Message.Should().Be($"An entry with label 'label1' or address '{address1}' already exist in the address book.");

                exception = secondAttempt.Should().Throw<FlurlHttpException>().Which;
                response = exception.Call.Response;
                errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(await response.ResponseMessage.Content.ReadAsStringAsync());
                errors = errorResponse.Errors;
                response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
                errors.Should().ContainSingle();
                errors.First().Message.Should().Be($"An entry with label 'label2' or address '{address1}' already exist in the address book.");

                exception = thirdAttempt.Should().Throw<FlurlHttpException>().Which;
                response = exception.Call.Response;
                errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(await response.ResponseMessage.Content.ReadAsStringAsync());
                errors = errorResponse.Errors;
                response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
                errors.Should().ContainSingle();
                errors.First().Message.Should().Be($"An entry with label 'label1' or address '{address2}' already exist in the address book.");
            }
        }

        [Fact]
        public async Task RemoveAnAddressBookEntryWhenNoSuchEntryExistsAsync()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                // Arrange.
                CoreNode node = builder.CreateStratisPosNode(this.network).Start();

                // Act.
                // Add an entry with the same address and label already exist.
                Func<Task> act = async () => await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook/address")
                    .SetQueryParams(new { label = "label1" })
                    .DeleteAsync()
                    .ReceiveJson<AddressBookEntryModel>();

                // Assert.
                var exception = act.Should().Throw<FlurlHttpException>().Which;
                var response = exception.Call.Response;
                ErrorResponse errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(await response.ResponseMessage.Content.ReadAsStringAsync());
                List<ErrorModel> errors = errorResponse.Errors;
                response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
                errors.Should().ContainSingle();
                errors.First().Message.Should().Be("No item with label 'label1' was found in the address book.");
            }
        }

        [Fact]
        public async Task RemoveAnAddressBookEntryWhenAnEntryExistsAsync()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                // Arrange.
                CoreNode node = builder.CreateStratisPosNode(this.network).Start();

                string address1 = new Key().PubKey.Hash.GetAddress(this.network).ToString();

                // Add a first address.
                AddressBookEntryModel newEntry = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook/address")
                    .PostJsonAsync(new { label = "label1", address = address1 })
                    .ReceiveJson<AddressBookEntryModel>();

                // Check the address is in the address book.
                AddressBookModel addressBook = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook")
                    .SetQueryParams(new { label = "label1" })
                    .GetJsonAsync<AddressBookModel>();

                addressBook.Addresses.Should().ContainSingle();
                addressBook.Addresses.Single().Label.Should().Be("label1");

                // Act.
                AddressBookEntryModel entryRemoved = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook/address")
                    .SetQueryParams(new { label = "label1" })
                    .DeleteAsync()
                    .ReceiveJson<AddressBookEntryModel>();

                // Assert.
                addressBook = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook")
                    .SetQueryParams(new { label = "label1" })
                    .GetJsonAsync<AddressBookModel>();

                addressBook.Addresses.Should().BeEmpty();
            }
        }

        [Fact]
        public async Task GetAnAddressBookAsync()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                // Arrange.
                CoreNode node = builder.CreateStratisPosNode(this.network).Start();

                string address1 = new Key().PubKey.Hash.GetAddress(this.network).ToString();
                string address2 = new Key().PubKey.Hash.GetAddress(this.network).ToString();
                string address3 = new Key().PubKey.Hash.GetAddress(this.network).ToString();
                string address4 = new Key().PubKey.Hash.GetAddress(this.network).ToString();
                string address5 = new Key().PubKey.Hash.GetAddress(this.network).ToString();

                // Add a few addresses.
                await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook/address")
                    .PostJsonAsync(new { label = "label1", address = address1 })
                    .ReceiveJson<AddressBookEntryModel>();

                await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook/address")
                    .PostJsonAsync(new { label = "label2", address = address2 })
                    .ReceiveJson<AddressBookEntryModel>();

                await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook/address")
                    .PostJsonAsync(new { label = "label3", address = address3 })
                    .ReceiveJson<AddressBookEntryModel>();

                await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook/address")
                    .PostJsonAsync(new { label = "label4", address = address4 })
                    .ReceiveJson<AddressBookEntryModel>();

                await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook/address")
                    .PostJsonAsync(new { label = "label5", address = address5 })
                    .ReceiveJson<AddressBookEntryModel>();

                // Act.
                AddressBookModel addressBook = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook")
                    .GetJsonAsync<AddressBookModel>();

                // Assert.
                addressBook.Addresses.Should().HaveCount(5);
                addressBook.Addresses.First().Label.Should().Be("label1");
                addressBook.Addresses.First().Address.Should().Be(address1);
                addressBook.Addresses.Last().Label.Should().Be("label5");
                addressBook.Addresses.Last().Address.Should().Be(address5);
            }
        }

        [Fact]
        public async Task GetAnAddressBookWithPaginationAsync()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                // Arrange.
                CoreNode node = builder.CreateStratisPosNode(this.network).Start();

                string address1 = new Key().PubKey.Hash.GetAddress(this.network).ToString();
                string address2 = new Key().PubKey.Hash.GetAddress(this.network).ToString();
                string address3 = new Key().PubKey.Hash.GetAddress(this.network).ToString();
                string address4 = new Key().PubKey.Hash.GetAddress(this.network).ToString();
                string address5 = new Key().PubKey.Hash.GetAddress(this.network).ToString();

                // Add a few addresses.
                await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook/address")
                    .PostJsonAsync(new { label = "label1", address = address1 })
                    .ReceiveJson<AddressBookEntryModel>();

                await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook/address")
                    .PostJsonAsync(new { label = "label2", address = address2 })
                    .ReceiveJson<AddressBookEntryModel>();

                await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook/address")
                    .PostJsonAsync(new { label = "label3", address = address3 })
                    .ReceiveJson<AddressBookEntryModel>();

                await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook/address")
                    .PostJsonAsync(new { label = "label4", address = address4 })
                    .ReceiveJson<AddressBookEntryModel>();

                await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook/address")
                    .PostJsonAsync(new { label = "label5", address = address5 })
                    .ReceiveJson<AddressBookEntryModel>();

                // Act.
                AddressBookModel queryResult = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook")
                    .SetQueryParams(new { skip = 0, take = 5 })
                    .GetJsonAsync<AddressBookModel>();

                // Assert.
                queryResult.Addresses.Should().HaveCount(5);

                // Act.
                queryResult = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook")
                    .SetQueryParams(new { skip = 2, take = 3 })
                    .GetJsonAsync<AddressBookModel>();

                // Assert.
                queryResult.Addresses.Should().HaveCount(3);
                queryResult.Addresses.First().Label.Should().Be("label3");
                queryResult.Addresses.First().Address.Should().Be(address3);
                queryResult.Addresses.Last().Label.Should().Be("label5");
                queryResult.Addresses.Last().Address.Should().Be(address5);

                // Act.
                queryResult = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("addressbook")
                    .SetQueryParams(new { skip = 2 })
                    .GetJsonAsync<AddressBookModel>();

                // Assert.
                queryResult.Addresses.Should().HaveCount(3);
                queryResult.Addresses.First().Label.Should().Be("label3");
                queryResult.Addresses.First().Address.Should().Be(address3);
                queryResult.Addresses.Last().Label.Should().Be("label5");
                queryResult.Addresses.Last().Address.Should().Be(address5);
            }
        }
    }
}
