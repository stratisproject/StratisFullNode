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
        private string loggerName;

        public Logger(NLog.Logger logger, string loggerName)
        {
            this.logger = logger;
            this.loggerName = loggerName;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            // The rest of the method cares about logging via NLog to files.
            NLog.LogLevel nLogLevel = logLevel.ToNLogLevel();
            if (!this.IsEnabled(logLevel))
                return;

            if (formatter == null)
                throw new ArgumentNullException(nameof(formatter));

            string message = formatter(state, exception);

            NLog.LogEventInfo eventInfo = NLog.LogEventInfo.Create(nLogLevel, this.logger.Name, message);
            eventInfo.Exception = exception;
            eventInfo.LoggerName = this.loggerName;
            this.logger.Log(eventInfo);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return this.logger.IsEnabled(logLevel.ToNLogLevel());
        }
    }
}
