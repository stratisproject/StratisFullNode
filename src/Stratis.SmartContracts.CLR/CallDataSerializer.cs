using System;
using System.Collections.Generic;
using System.Linq;
using CSharpFunctionalExtensions;
using NBitcoin;
using Nethereum.RLP;
using Stratis.Bitcoin.Utilities;
using Stratis.SmartContracts.CLR.Serialization;
using Stratis.SmartContracts.Core;
using TracerAttributes;

namespace Stratis.SmartContracts.CLR
{
    [NoTrace]
    public class CallDataSerializer : ICallDataSerializer
    {
        public const int OpcodeSize = sizeof(byte);
        public const int VmVersionSize = sizeof(int);
        public const int GasPriceSize = sizeof(ulong);
        public const int GasLimitSize = sizeof(ulong);
        public const int AddressSize = 20;
        public const int PrefixSize = OpcodeSize + VmVersionSize + GasPriceSize + GasLimitSize;
        public const int CallContractPrefixSize = PrefixSize + AddressSize;

        private readonly IMethodParameterSerializer methodParamSerializer;
        private readonly IContractPrimitiveSerializer primitiveSerializer;

        public CallDataSerializer(IContractPrimitiveSerializer primitiveSerializer)
        {            
            this.primitiveSerializer = primitiveSerializer;
            this.methodParamSerializer = new MethodParameterByteSerializer(primitiveSerializer);
        }

        public Result<ContractTxData> Deserialize(byte[] smartContractBytes)
        {
            try
            {
                byte type = smartContractBytes[0];
                byte[] vmVersionBytes = smartContractBytes.Slice(OpcodeSize, VmVersionSize);
                byte[] gasPriceBytes = smartContractBytes.Slice(OpcodeSize + VmVersionSize, GasPriceSize);
                byte[] gasLimitBytes = smartContractBytes.Slice(OpcodeSize + VmVersionSize + GasPriceSize, GasLimitSize);                
                
                int vmVersion = this.primitiveSerializer.Deserialize<int>(vmVersionBytes);
                ulong gasPrice = this.primitiveSerializer.Deserialize<ulong>(gasPriceBytes);
                var gasLimit = (RuntimeObserver.Gas) this.primitiveSerializer.Deserialize<ulong>(gasLimitBytes);

                return IsCallContract(type) 
                    ? this.SerializeCallContract(smartContractBytes, vmVersion, gasPrice, gasLimit)
                    : this.SerializeCreateContract(smartContractBytes, vmVersion, gasPrice, gasLimit);
                
            }
            catch (Exception e)
            {
                // TODO: Avoid this catch all exceptions
                return Result.Fail<ContractTxData>("Error deserializing calldata. " + e.Message);
            }
        }

        protected virtual Result<ContractTxData> SerializeCreateContract(byte[] smartContractBytes, int vmVersion, ulong gasPrice, RuntimeObserver.Gas gasLimit)
        {
            byte[] remaining = smartContractBytes.Slice(PrefixSize, (uint) (smartContractBytes.Length - PrefixSize));

            IList<byte[]> decodedParams = RLPDecode(remaining);

            var contractExecutionCode = this.primitiveSerializer.Deserialize<byte[]>(decodedParams[0]);
            object[] methodParameters = this.DeserializeMethodParameters(decodedParams[1]);
            string[] signatures = (decodedParams.Count > 2) ? this.DeserializeSignatures(decodedParams[2]) : null;

            var callData = new ContractTxData(vmVersion, gasPrice, gasLimit, contractExecutionCode, methodParameters, signatures);
            return Result.Ok(callData);
        }

        public Result<ContractTxData> SerializeCallContract(byte[] smartContractBytes, int vmVersion, ulong gasPrice, RuntimeObserver.Gas gasLimit)
        {
            byte[] contractAddressBytes = smartContractBytes.Slice(PrefixSize, AddressSize);
            var contractAddress = new uint160(contractAddressBytes);

            byte[] remaining = smartContractBytes.Slice(CallContractPrefixSize,
                (uint) (smartContractBytes.Length - CallContractPrefixSize));

            IList<byte[]> decodedParams = RLPDecode(remaining);

            string methodName = this.primitiveSerializer.Deserialize<string>(decodedParams[0]);
            object[] methodParameters = this.DeserializeMethodParameters(decodedParams[1]);
            string[] signatures = (decodedParams.Count > 2) ? this.DeserializeSignatures(decodedParams[2]) : null;

            var callData = new ContractTxData(vmVersion, gasPrice, gasLimit, contractAddress, methodName, methodParameters, signatures);
            return Result.Ok(callData);
        }

        protected static IList<byte[]> RLPDecode(byte[] remaining)
        {
            RLPCollection list = RLP.Decode(remaining);

            RLPCollection innerList = (RLPCollection) list[0];

            return innerList.Select(x => x.RLPData).ToList();
        }

        public byte[] Serialize(ContractTxData contractTxData)
        {
            return IsCallContract(contractTxData.OpCodeType) 
                ? this.SerializeCallContract(contractTxData) 
                : this.SerializeCreateContract(contractTxData);
        }

        private byte[] SerializeCreateContract(ContractTxData contractTxData)
        {
            var rlpBytes = new List<byte[]>();

            rlpBytes.Add(contractTxData.ContractExecutionCode);
            
            this.AddMethodParams(rlpBytes, contractTxData.MethodParameters);
            if (contractTxData.Signatures != null)
                this.AddSignatures(rlpBytes, contractTxData.Signatures);
            
            byte[] encoded = RLP.EncodeList(rlpBytes.Select(RLP.EncodeElement).ToArray());
            
            var bytes = new byte[PrefixSize + encoded.Length];

            this.SerializePrefix(bytes, contractTxData);
            
            encoded.CopyTo(bytes, PrefixSize);

            return bytes;
        }

        private byte[] SerializeCallContract(ContractTxData contractTxData)
        {
            var rlpBytes = new List<byte[]>();

            rlpBytes.Add(this.primitiveSerializer.Serialize(contractTxData.MethodName));

            this.AddMethodParams(rlpBytes, contractTxData.MethodParameters);
            if (contractTxData.Signatures != null)
                this.AddSignatures(rlpBytes, contractTxData.Signatures);

            byte[] encoded = RLP.EncodeList(rlpBytes.Select(RLP.EncodeElement).ToArray());
            
            var bytes = new byte[CallContractPrefixSize + encoded.Length];

            this.SerializePrefix(bytes, contractTxData);

            contractTxData.ContractAddress.ToBytes().CopyTo(bytes, PrefixSize);

            encoded.CopyTo(bytes, CallContractPrefixSize);

            return bytes;
        }

        protected void SerializePrefix(byte[] bytes, ContractTxData contractTxData)
        {
            byte[] vmVersion = this.primitiveSerializer.Serialize(contractTxData.VmVersion);
            byte[] gasPrice = this.primitiveSerializer.Serialize(contractTxData.GasPrice);
            byte[] gasLimit = this.primitiveSerializer.Serialize(contractTxData.GasLimit.Value);
            bytes[0] = contractTxData.OpCodeType;
            vmVersion.CopyTo(bytes, OpcodeSize);
            gasPrice.CopyTo(bytes, OpcodeSize + VmVersionSize);
            gasLimit.CopyTo(bytes, OpcodeSize + VmVersionSize + GasPriceSize);
        }

        protected void AddMethodParams(List<byte[]> rlpBytes, object[] methodParameters)
        {
            if (methodParameters != null && methodParameters.Any())
            {
                rlpBytes.Add(this.SerializeMethodParameters(methodParameters));
            }
            else
            {
                rlpBytes.Add(new byte[0]);
            }
        }

        /// <summary>
        /// Adds the passed signatures to the passed list of byte arrays.
        /// </summary>
        /// <param name="rlpBytes">The list of byte arrays to add the signatures to.</param>
        /// <param name="signatures">The signatures as a base 64 encoded byte array. See <see cref="SerializeSignatures(string[])"/></param>
        protected void AddSignatures(List<byte[]> rlpBytes, string[] signatures)
        {
            Guard.NotNull(signatures, nameof(signatures));

            rlpBytes.Add(this.SerializeSignatures(signatures));
        }

        protected static bool IsCallContract(byte type)
        {
            return type == (byte)ScOpcodeType.OP_CALLCONTRACT;
        }

        protected byte[] SerializeMethodParameters(object[] objects)
        {
            return this.methodParamSerializer.Serialize(objects);
        }

        protected object[] DeserializeMethodParameters(byte[] methodParametersRaw)
        {
            object[] methodParameters = null;

            if (methodParametersRaw != null && methodParametersRaw.Length > 0)
                methodParameters = this.methodParamSerializer.Deserialize(methodParametersRaw);

            return methodParameters;
        }

        /// <summary>
        /// Serializes the signatures.
        /// </summary>
        /// <param name="signatures">Signatures passed as an array of base 64 encoded byte arrays.</param>
        /// <returns>A byte array containing the decoded signatures, where each signature is prefixed by its length.</returns>
        protected byte[] SerializeSignatures(string[] signatures)
        {
            byte[][] signaturesRaw = new byte[signatures.Length][];
            int totalBytes = 0;
            for (int i = 0; i < signatures.Length; i++)
            {
                signaturesRaw[i] = Convert.FromBase64String(signatures[i]);
                totalBytes += signaturesRaw[i].Length + 1;
            }

            var res = new byte[totalBytes];
            totalBytes = 0;
            for (int i = 0; i < signatures.Length; i++)
            {
                res[totalBytes] = (byte)signaturesRaw[i].Length;
                signaturesRaw[i].CopyTo(res, totalBytes + 1);
                totalBytes += signaturesRaw[i].Length + 1;
            }

            return res;
        }

        /// <summary>
        /// Deserializes signatures.
        /// </summary>
        /// <param name="signaturesRaw">A byte array containing the decoded signatures, where each signature is prefixed by its length.</param>
        /// <returns>Signatures as an array of base 64 encoded byte arrays.</returns>
        protected string[] DeserializeSignatures(byte[] signaturesRaw)
        {
            if (signaturesRaw == null || signaturesRaw.Length == 0)
                return new string[0];

            var signatures = new List<string>();

            for (int i = 0; i < signaturesRaw.Length; i++)
            {
                int length = signaturesRaw[i];
                var buffer = new byte[length];
                Array.Copy(signaturesRaw, i + 1, buffer, 0, length);
                i += length;
                signatures.Add(Convert.ToBase64String(buffer));
            }

            return signatures.ToArray();
        }
    }
}