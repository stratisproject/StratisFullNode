﻿using NBitcoin;
using NBitcoin.Crypto;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Networks;
using Xunit;
using Key = NBitcoin.Key;

namespace Stratis.SmartContracts.IntegrationTests
{
    public class ECRecoverTests
    {
        private readonly Network network;
        private readonly EcRecoverProvider ecRecover;

        public ECRecoverTests()
        {
            this.network = new SmartContractsPoARegTest();
            this.ecRecover = new EcRecoverProvider(this.network);
        }

        // 2 things to test: 

        // 1) That we have the ECDSA code and can make it available.

        [Fact]
        public void CanSignAndRetrieveSender()
        {
            Key privateKey = new Key();
            Address address = privateKey.PubKey.GetAddress(this.network).ToString().ToAddress(this.network);
            byte[] message = new byte[] { 0x69, 0x76, 0xAA };

            // Sign a message
            ECDSASignature offChainSignature = EcRecoverProvider.SignMessage(privateKey, message);

            // Get the address out of the signature
            Address recoveredAddress = this.ecRecover.GetSigner(offChainSignature.ToDER(), message);

            // Check that the address matches that generated from the private key.
            Assert.Equal(address, recoveredAddress);
        }

        // 2) That we can enable the method in new contracts without affecting the older contracts

        // TODO

    }
}
