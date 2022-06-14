﻿using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Features.BlockStore.Models;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Interfaces;

namespace Stratis.Bitcoin.Features.BlockStore.AddressIndexing
{
    public class AddressIndexerCV : IAddressIndexer
    {
        private readonly Network network;
        private readonly ICoinView coinView;
        private readonly ChainIndexer chainIndexer;
        private readonly IScriptAddressReader scriptAddressReader;

        public ChainedHeader IndexerTip => GetTip();

        public IFullNodeFeature InitializingFeature { set; private get; }

        public AddressIndexerCV(Network network, ChainIndexer chainIndexer, IScriptAddressReader scriptAddressReader, ICoinView coinView)
        {
            this.network = network;
            this.coinView = coinView;
            this.chainIndexer = chainIndexer;
            this.scriptAddressReader = scriptAddressReader;
        }

        private ChainedHeader GetTip()
        {
            this.coinView.Sync(this.chainIndexer);

            return this.chainIndexer[this.coinView.GetTipHash().Hash];
        }

        public void Initialize()
        {
        }

        private TxDestination AddressToDestination(string address)
        {
            var bitcoinAddress = BitcoinAddress.Create(address, this.network);
            return this.scriptAddressReader.GetDestinationFromScriptPubKey(this.network, bitcoinAddress.ScriptPubKey).Single();
        }

        public AddressBalancesResult GetAddressBalances(string[] addresses, int minConfirmations = 0)
        {
            return new AddressBalancesResult()
            {
                Balances = addresses.Select(address => new AddressBalanceResult()
                {
                    Address = address,
                    Balance = new Money(this.coinView.GetBalance(AddressToDestination(address)).First().satoshis)
                }).ToList()
            };
        }

        public LastBalanceDecreaseTransactionModel GetLastBalanceDecreaseTransaction(string address)
        {
            throw new NotImplementedException();
        }

        private IEnumerable<AddressBalanceChange> ToDiff(List<AddressBalanceChange> addressBalanceChanges)
        {
            for (int i = addressBalanceChanges.Count - 1; i > 0; i--)
            {
                yield return new AddressBalanceChange() { 
                    BalanceChangedHeight = addressBalanceChanges[i].BalanceChangedHeight,
                    Deposited = addressBalanceChanges[i].Satoshi < addressBalanceChanges[i - 1].Satoshi,
                    Satoshi = Math.Abs(addressBalanceChanges[i].Satoshi - addressBalanceChanges[i - 1].Satoshi)
                };
            }
        }

        /// <inheritdoc/>
        public VerboseAddressBalancesResult GetAddressIndexerState(string[] addresses)
        {
            // If the containing feature is not initialized then wait a bit.
            this.InitializingFeature?.WaitInitialized();

            return new VerboseAddressBalancesResult(this.IndexerTip.Height)
            {
                BalancesData = addresses.Select(address => new AddressIndexerData()
                {
                    Address = address,                    
                    BalanceChanges = ToDiff(this.coinView.GetBalance(AddressToDestination(address)).Select(b => new AddressBalanceChange()
                    {
                         BalanceChangedHeight = (int)b.height,
                         Deposited = b.satoshis >= 0,
                         Satoshi = Math.Abs(b.satoshis)
                    }).ToList()).ToList()
                }).ToList()
            };
        }

        public void Dispose()
        {
        }
    }
}
