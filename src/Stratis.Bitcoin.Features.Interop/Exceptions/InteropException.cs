using System;

namespace Stratis.Bitcoin.Features.Interop.Exceptions
{
    /// <summary>
    /// A general exception indicating an error with the interop process.
    /// </summary>
    public sealed class InteropException : Exception
    {
        public InteropException()
        {
        }

        public InteropException(string message) : base(message)
        {
        }
    }
}
