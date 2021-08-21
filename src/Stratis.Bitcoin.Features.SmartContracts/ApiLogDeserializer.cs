using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Text;
using NBitcoin;
using Nethereum.RLP;
using Stratis.Bitcoin.Features.SmartContracts.Models;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Caching;
using Stratis.SmartContracts.CLR.Loader;
using Stratis.SmartContracts.CLR.Serialization;
using Stratis.SmartContracts.Core.Receipts;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Bitcoin.Features.SmartContracts
{
    /// <summary>
    /// Deserializer for smart contract event logs. 
    /// </summary>
    public class ApiLogDeserializer
    {
        private readonly IContractPrimitiveSerializer primitiveSerializer;
        private readonly Network network;
        private readonly IStateRepositoryRoot stateRepositoryRoot;
        private readonly IContractAssemblyCache contractAssemblyCache;

        public ApiLogDeserializer(IContractPrimitiveSerializer primitiveSerializer, Network network, IStateRepositoryRoot stateRepositoryRoot, IContractAssemblyCache contractAssemblyCache)
        {
            this.primitiveSerializer = primitiveSerializer;
            this.network = network;
            this.stateRepositoryRoot = stateRepositoryRoot;
            this.contractAssemblyCache = contractAssemblyCache;
        }

        public List<LogResponse> MapLogResponses(Log[] logs)
        {
            var logResponses = new List<LogResponse>();

            foreach (Log log in logs)
            {
                var logResponse = new LogResponse(log, this.network);

                logResponses.Add(logResponse);

                if (log.Topics.Count == 0)
                    continue;

                // logResponse.Address is the address of the contract that generated the log.
                Assembly assembly = GetAssembly(logResponse.Address.ToUint160(this.network));

                if (assembly == null)
                {
                    // Couldn't load the assembly - this is highly unexpected because we would have already used it to execute a tx.
                    // Fine to throw an exception here as this should only be used in the API.
                    throw new Exception($"Unable to read logs - contract at {logResponse.Address} is missing from state database");
                }

                // Get receipt struct name
                string eventTypeName = Encoding.UTF8.GetString(log.Topics[0]);

                // Find the type in the module def
                Type eventType = assembly.DefinedTypes.FirstOrDefault(t => t.Name == eventTypeName);

                if (eventType == null)
                {
                    // Couldn't match the type, continue?
                    throw new Exception($"Unable to read logs - contract at {logResponse.Address} has no event type {eventTypeName}");
                }

                // Deserialize it
                dynamic deserialized = DeserializeLogData(log.Data, eventType);

                logResponse.Log = deserialized;
            }

            return logResponses;
        }

        private Assembly GetAssembly(uint160 address)
        {
            var codeHashBytes = this.stateRepositoryRoot.GetCodeHash(address);

            if (codeHashBytes == null)
                return null;

            var codeHash = new uint256(codeHashBytes);

            // Attempt to load from cache.
            CachedAssemblyPackage cachedAssembly = this.contractAssemblyCache.Retrieve(codeHash);

            if (cachedAssembly == null)
            {
                // Cache is not thread-safe so don't load into the cache if not found - leave that for consensus for now.
                var byteCode = this.stateRepositoryRoot.GetCode(address);
                
                if (byteCode == null)
                {
                    return null;
                }
                
                return Assembly.Load(byteCode);
            }

            return cachedAssembly.Assembly.Assembly;            
        }

        /// <summary>
        /// Deserializes event log data. Uses the supplied type to determine field information and attempts to deserialize these
        /// fields from the supplied data. For <see cref="Address"/> types, an additional conversion to a base58 string is applied.
        /// </summary>
        /// <param name="bytes">The raw event log data.</param>
        /// <param name="type">The type to attempt to deserialize.</param>
        /// <returns>An <see cref="ExpandoObject"/> containing the fields of the Type and its deserialized values.</returns>
        public dynamic DeserializeLogData(byte[] bytes, Type type)
        {
            RLPCollection collection = (RLPCollection)RLP.Decode(bytes);

            var instance = new ExpandoObject() as IDictionary<string, object>;

            FieldInfo[] fields = type.GetFields();

            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                byte[] fieldBytes = collection[i].RLPData;
                Type fieldType = field.FieldType;

                if (fieldType == typeof(Address))
                {
                    string base58Address = new uint160(fieldBytes).ToBase58Address(this.network);

                    instance[field.Name] = base58Address;
                }
                else
                {
                    object fieldValue = this.primitiveSerializer.Deserialize(fieldType, fieldBytes);

                    if (fieldType == typeof(UInt128) || fieldType == typeof(UInt256))
                        fieldValue = fieldValue.ToString();

                    instance[field.Name] = fieldValue;
                }
            }

            return instance;
        }
    }
}