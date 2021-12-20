using System;
using Microsoft.Extensions.Logging;

namespace Stratis.Bitcoin.Configuration.Logging
{
    public static class ILoggerExt
    {
        public static void Debug(this ILogger logger, string message, params object[] args)
        {
            logger.Log(LogLevel.Debug, default, args, null, (s, e) => string.Format(message, s));
        }

        public static void Info(this ILogger logger, string message, params object[] args)
        {
            logger.Log(LogLevel.Information, default, args, null, (s, e) => string.Format(message, s));
        }

        public static void Trace(this ILogger logger, string message, params object[] args)
        {
            logger.Log(LogLevel.Trace, default, args, null, (s, e) => string.Format(message, s));
        }

        public static void Warn(this ILogger logger, string message, params object[] args)
        {
            logger.Log(LogLevel.Warning, default, args, null, (s, e) => string.Format(message, s));
        }

        public static void Warn(this ILogger logger, Exception e, string message, params object[] args)
        {
            logger.Log(LogLevel.Warning, default, args, e, (s, e) => string.Format(message, s));
        }

        public static void Error(this ILogger logger, string message, params object[] args)
        {
            logger.Log(LogLevel.Error, default, args, null, (s, e) => string.Format(message, s));
        }

        public static void Error(this ILogger logger, Exception e, string message, params object[] args)
        {
            logger.Log(LogLevel.Error, default, args, e, (s, e) => string.Format(message, s));
        }

        public static void Fatal(this ILogger logger, string message, params object[] args)
        {
            logger.Log(LogLevel.Critical, default, args, null, (s, e) => string.Format(message, s));
        }
    }
}
