using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stratis.Bitcoin.Utilities.JsonConverters;

namespace Stratis.Bitcoin.Features.RPC
{
    public class RPCRequest
    {
        public RPCRequest(RPCOperations method, object[] parameters) : this(method.ToString(), parameters)
        {
        }

        public RPCRequest(string method, object[] parameters) : this()
        {
            this.Method = method;
            this.Params = parameters;
        }

        public RPCRequest()
        {
            this.JsonRpc = "1.0";
        }

        public string JsonRpc { get; set; }

        public string Id { get; set; }

        public string Method { get; set; }

        public object[] Params { get; set; }

        public void WriteJSON(TextWriter writer)
        {
            using (var jsonWriter = new JsonTextWriter(writer))
            {
                jsonWriter.CloseOutput = false;
                WriteJSON(jsonWriter);
                jsonWriter.Flush();
            }
        }

        internal void WriteJSON(JsonTextWriter writer)
        {
            writer.WriteStartObject();
            WriteProperty(writer, "jsonrpc", this.JsonRpc);
            WriteProperty(writer, "id", this.Id);
            WriteProperty(writer, "method", this.Method);

            writer.WritePropertyName("params");
            writer.WriteStartArray();

            if (this.Params == null)
            {
                writer.WriteEndArray();
                writer.WriteEndObject();
                return;
            }

            for (int i = 0; i < this.Params.Length; i++)
            {
                if (this.Params[i] is JToken)
                {
                    ((JToken) this.Params[i]).WriteTo(writer);
                }
                else if (this.Params[i] is Array)
                {
                    writer.WriteStartArray();

                    foreach (object x in (Array) this.Params[i])
                    {
                        // Primitive types are handled well by the writer's WriteValue method, but classes need to be serialised using the same converter set as the rest of the codebase.
                        WriteValueOrSerializeAndWrite(writer, x);
                    }

                    writer.WriteEndArray();
                }
                else
                {
                    WriteValueOrSerializeAndWrite(writer, this.Params[i]);
                }
            }
            
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private void WriteValueOrSerializeAndWrite(JsonTextWriter writer, object valueToWrite)
        {
            if (valueToWrite == null || valueToWrite.GetType().IsValueType)
            {
                writer.WriteValue(valueToWrite);
                return;
            }

            // TODO: It did not appear that the RPC subsystem was automatically handling complex class parameters in requests. So we will need to start passing the network into the RPCRequest constructor to properly handle every possible type
            JToken token = Serializer.ToToken(valueToWrite);
            token.WriteTo(writer);
        }

        private void WriteProperty<TValue>(JsonTextWriter writer, string property, TValue value)
        {
            writer.WritePropertyName(property);
            writer.WriteValue(value);
        }
    }
}
