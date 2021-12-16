using System;

namespace Stratis.Bitcoin.Configuration.Logging
{
    public class NullLogger : ILogger
    {
        public static NullLogger Instance = new NullLogger();

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return false;
        }
    }
}
