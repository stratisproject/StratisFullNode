using System.Collections.Generic;
using CSharpFunctionalExtensions;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Interfaces;
using Stratis.SmartContracts.CLR;

namespace Stratis.Bitcoin.Features.SmartContracts
{
    /// <summary>
    /// Smart contract specific logic to get the contract address from the <see cref="ContractTxData"/>.
    /// </summary>
    public sealed class SmartContractScriptAddressReader : IScriptDestinationReader
    {
        private readonly IScriptAddressReader baseAddressReader;
        private readonly ICallDataSerializer callDataSerializer;

        public SmartContractScriptAddressReader(
            ICallDataSerializer callDataSerializer,
            ScriptAddressReader scriptAddressReader,
            ScriptDestinationReader scriptDestinationReader = null)
        {
            this.baseAddressReader = scriptDestinationReader ?? (IScriptAddressReader)scriptAddressReader;
            this.callDataSerializer = callDataSerializer;
        }

        public IEnumerable<TxDestination> GetDestinationFromScriptPubKey(Network network, Script script)
        {
            if (script.IsSmartContractCreate() || script.IsSmartContractCall())
            {
                Result<ContractTxData> result = this.callDataSerializer.Deserialize(script.ToBytes());
                if (result.Value.ContractAddress != null)
                {
                    string address = result.Value.ContractAddress.ToAddress().ToString(); 
                    TxDestination destination = ScriptDestinationReader.GetDestinationForAddress(address, network);
                    if (destination != null)
                        yield return destination;
                }
            }
            else
            {
                if (this.baseAddressReader is IScriptDestinationReader destinationReader)
                {
                    foreach (TxDestination destination in destinationReader.GetDestinationFromScriptPubKey(network, script))
                        if (destination != null)
                            yield return destination;
                }
                else
                {
                    TxDestination destination = ScriptDestinationReader.GetDestinationForAddress(this.baseAddressReader.GetAddressFromScriptPubKey(network, script), network);
                    if (destination != null)
                        yield return destination;
                }
            }
        }

        public string GetAddressFromScriptPubKey(Network network, Script script)
        {
            if (script.IsSmartContractCreate() || script.IsSmartContractCall())
            {
                Result<ContractTxData> result = this.callDataSerializer.Deserialize(script.ToBytes());
                return result.Value.ContractAddress?.ToAddress().ToString();
            }

            return this.baseAddressReader.GetAddressFromScriptPubKey(network, script);
        }
    }
}