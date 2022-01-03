using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NBitcoin;
using Nethereum.RLP;
using Stratis.SmartContracts.CLR.Exceptions;
using TracerAttributes;

namespace Stratis.SmartContracts.CLR.Serialization
{
    /// <summary>
    /// This class serializes and deserializes specific data types
    /// when persisting items inside a smart contract.
    /// 
    /// V1 returns empty strings rather than nulls.
    /// </summary>
    [NoTrace]
    public class ContractPrimitiveSerializerV2 : IContractPrimitiveSerializer
    {
        private readonly Network network;

        public ContractPrimitiveSerializerV2(Network network)
        {
            this.network = network;
        }

        public byte[] Serialize(object o)
        {
            return o switch
            {
                null => null,
                byte[] bytes => bytes,
                Array array => Serialize(array),
                byte b1 => new byte[] { b1 },
                char c => Serialize(c),
                Address address => Serialize(address),
                bool b => Serialize(b),
                int i => Serialize(i),
                long l => Serialize(l),
                UInt128 u => Serialize(u),
                UInt256 u => Serialize(u),
                uint u => Serialize(u),
                ulong u => Serialize(u),
                string s => Serialize(s),
                _ when o.GetType().IsValueType => SerializeStruct(o),
                _ => throw new ContractPrimitiveSerializationException(string.Format("{0} is not supported.", o.GetType().Name))
            };
        }

        #region Primitive serialization

        private byte[] Serialize(Address address)
        {
            return address.ToBytes();
        }

        private byte[] Serialize(bool b)
        {
            return BitConverter.GetBytes(b);
        }

        private byte[] Serialize(int i)
        {
            return BitConverter.GetBytes(i);
        }

        private byte[] Serialize(long l)
        {
            return BitConverter.GetBytes(l);
        }

        private byte[] Serialize(uint u)
        {
            return BitConverter.GetBytes(u);
        }

        private byte[] Serialize(ulong ul)
        {
            return BitConverter.GetBytes(ul);
        }

        private byte[] Serialize(UInt128 u128)
        {
            return u128.ToBytes();
        }

        private byte[] Serialize(UInt256 u256)
        {
            return u256.ToBytes();
        }

        private byte[] Serialize(char c)
        {
            return BitConverter.GetBytes(c);
        }

        private byte[] Serialize(string s)
        {
            return Encoding.UTF8.GetBytes(s);
        }

        #endregion

        private byte[] SerializeStruct(object o)
        {
            List<byte[]> toEncode = new List<byte[]>(); 

            foreach (FieldInfo field in o.GetType().GetFields())
            {
                object value = field.GetValue(o);
                byte[] serialized = this.Serialize(value);
                toEncode.Add(RLP.EncodeElement(serialized));
            }

            return RLP.EncodeList(toEncode.ToArray());
        }

        private byte[] Serialize(Array array)
        {
            // Edge case, serializing nonsensical
            if (array is byte[] a)
                return a;

            List<byte[]> toEncode = new List<byte[]>();

            for(int i=0; i< array.Length; i++)
            {
                object value = array.GetValue(i);
                byte[] serialized = this.Serialize(value);
                toEncode.Add(RLP.EncodeElement(serialized));
            }

            return RLP.EncodeList(toEncode.ToArray());
        }

        public T Deserialize<T>(byte[] stream)
        {
            object deserialized = this.Deserialize(typeof(T), stream);

            return (T) deserialized;
        }

        public virtual object Deserialize(Type type, byte[] stream)
        {
            if (stream == null)
                return null;

            return DeserializeBytes(type, stream);
        }

        protected object DeserializeBytes(Type type, byte[] stream)
        {
            return Type.GetTypeCode(type) switch
            {
                TypeCode.Byte => stream[0],
                TypeCode.Char => ToChar(stream),
                TypeCode.Boolean => ToBool(stream),
                TypeCode.Int32 => ToInt32(stream),
                TypeCode.Int64 => ToInt64(stream),
                TypeCode.String => ToString(stream),
                TypeCode.UInt32 => ToUInt32(stream),
                TypeCode.UInt64 => ToUInt64(stream),
                _ when type == typeof(byte[]) => stream,
                _ when type == typeof(UInt128) => ToUInt128(stream),
                _ when type == typeof(UInt256) => ToUInt256(stream),
                _ when type == typeof(Address) => ToAddress(stream),
                _ when type.IsArray => DeserializeArray(type.GetElementType(), stream),
                _ when type.IsValueType => DeserializeStruct(type, stream),
                _ => throw new ContractPrimitiveSerializationException(string.Format("{0} is not supported.", type.Name)),
            };
        }

        public Address ToAddress(string address)
        {            
            return address.ToAddress(this.network);
        }

        #region Primitive Deserialization

        private bool ToBool(byte[] val)
        {
            return BitConverter.ToBoolean(val);
        }

        private Address ToAddress(byte[] val)
        {
            return val.ToAddress();
        }

        private int ToInt32(byte[] val)
        {
            return BitConverter.ToInt32(val, 0);
        }

        private uint ToUInt32(byte[] val)
        {
            return BitConverter.ToUInt32(val, 0);
        }

        private long ToInt64(byte[] val)
        {
            return BitConverter.ToInt64(val, 0);
        }

        private ulong ToUInt64(byte[] val)
        {
            return BitConverter.ToUInt64(val, 0);
        }

        private UInt128 ToUInt128(byte[] val)
        {
            return new UInt128(val);
        }

        private UInt256 ToUInt256(byte[] val)
        {
            return new UInt256(val);
        }

        private char ToChar(byte[] val)
        {
            return BitConverter.ToChar(val, 0);
        }

        private string ToString(byte[] val)
        {
            return Encoding.UTF8.GetString(val);
        }

        #endregion

        private object DeserializeStruct(Type type, byte[] bytes)
        {
            RLPCollection collection = (RLPCollection) RLP.Decode(bytes);

            object ret = Activator.CreateInstance(type);

            FieldInfo[] fields = type.GetFields();

            for (int i = 0; i < fields.Length; i++)
            {
                byte[] fieldBytes = collection[i].RLPData;
                fields[i].SetValue(ret, this.Deserialize(fields[i].FieldType, fieldBytes));
            }

            return ret;
        }

        private object DeserializeArray(Type elementType, byte[] bytes)
        {
            // Edge case, serializing nonsensical
            if (elementType == typeof(byte))
                return bytes;

            RLPCollection collection = (RLPCollection)RLP.Decode(bytes);

            Array ret = Array.CreateInstance(elementType, collection.Count);

            for(int i=0; i< collection.Count; i++)
            {
                ret.SetValue(this.Deserialize(elementType, collection[i].RLPData), i);
            }

            return ret;
        }
    }
}
