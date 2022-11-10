﻿using System.Collections.Generic;
using CSharpFunctionalExtensions;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Interfaces;
using Stratis.SmartContracts.CLR;

namespace Stratis.Bitcoin.Features.SmartContracts
{
    /// <summary>
    /// Smart contract specific logic to get the contract address from the <see cref="ContractTxData"/>.
    /// </summary>
    public sealed class SmartContractScriptAddressReader : IScriptAddressReader
    {
        private readonly IScriptAddressReader baseAddressReader;
        private readonly ICallDataSerializer callDataSerializer;

        public SmartContractScriptAddressReader(
            IScriptAddressReader addressReader,
            ICallDataSerializer callDataSerializer)
        {
            this.baseAddressReader = addressReader ?? new ScriptAddressReader();
            this.callDataSerializer = callDataSerializer;
        }

        public string GetAddressFromScriptPubKey(ScriptTemplate scriptTemplate, Network network, Script script)
        {
            if (script.IsSmartContractCreate() || script.IsSmartContractCall())
            {
                Result<ContractTxData> result = this.callDataSerializer.Deserialize(script.ToBytes());
                return result.Value.ContractAddress?.ToAddress().ToString();
            }

            return this.baseAddressReader.GetAddressFromScriptPubKey(network, script);
        }

        public IEnumerable<TxDestination> GetDestinationFromScriptPubKey(ScriptTemplate scriptTemplate, Script script)
        {
            if (script.IsSmartContractCreate() || script.IsSmartContractCall())
            {
                IEnumerable<TxDestination> Destinations()
                {
                    Result<ContractTxData> result = this.callDataSerializer.Deserialize(script.ToBytes());
                    yield return new KeyId(result.Value.ContractAddress);
                }

                return Destinations();
            }

            return this.baseAddressReader.GetDestinationFromScriptPubKey(scriptTemplate, script);
        }
    }
}