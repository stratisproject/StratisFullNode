using System;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Configuration.Logging
{
    public interface ILogger
    {
        void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter);

        bool IsEnabled(LogLevel logLevel);
    }

    public interface ILogger<T> : ILogger
    {
    }

    public class Logger : ILogger
    {
        private NLog.Logger logger;

        public Logger(NLog.Logger logger)
        {
            this.logger = logger;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!this.IsEnabled(logLevel))
                return;

            if (formatter == null)
                throw new ArgumentNullException(nameof(formatter));

            string message = formatter(state, exception);

            NLog.LogEventInfo eventInfo = NLog.LogEventInfo.Create(logLevel.ToNLogLevel(), this.logger.Name, message, exception);
            this.logger.Log(eventInfo);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return this.logger.IsEnabled(logLevel.ToNLogLevel());
        }
    }
}
