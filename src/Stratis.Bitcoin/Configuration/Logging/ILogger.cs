using System;
using Microsoft.Extensions.Logging;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Configuration.Logging
{
    public class Logger : ILogger
    {
        private NLog.Logger logger;

        public Logger(NLog.Logger logger)
        {
            this.logger = logger;
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel)
        {
            return this.logger.IsEnabled(logLevel.ToNLogLevel());
        }

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            return NLog.NestedDiagnosticsLogicalContext.Push(state);
        }
    }
}
