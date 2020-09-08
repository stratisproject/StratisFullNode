using Newtonsoft.Json;

namespace Stratis.Bitcoin.Utilities
{
    /// <summary>
    /// A generic result type that can be serialized.
    /// </summary>
    /// <typeparam name="T">The type of the value to return if the result was successful.</typeparam>
    public sealed class SerializableResult<T>
    {
        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("value")]
        public T Value { get; set; }

        public SerializableResult()
        {
        }

        private SerializableResult(T value, string message)
        {
            this.Message = message;
            this.Value = value;
        }

        public static SerializableResult<T> Ok(T value, string message = null)
        {
            return new SerializableResult<T>(value, message);
        }

        public static SerializableResult<T> Fail(string message)
        {
            return new SerializableResult<T>(default, message);
        }
    }
}