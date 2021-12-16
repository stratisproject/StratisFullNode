using System;

namespace Stratis.Bitcoin.Configuration.Logging
{
    public static class ILoggerExt
    {
        public static void Debug(this ILogger logger, string message, params object[] args)
        {
            logger.Log(LogLevel.Debug, null, args, null, (s, e) => string.Format(message, s));
        }

        public static void Info(this ILogger logger, string message, params object[] args)
        {
            logger.Log(LogLevel.Information, null, args, null, (s, e) => string.Format(message, s));
        }

        public static void Trace(this ILogger logger, string message, params object[] args)
        {
            logger.Log(LogLevel.Trace, null, args, null, (s, e) => string.Format(message, s));
        }

        public static void Warn(this ILogger logger, string message, params object[] args)
        {
            logger.Log(LogLevel.Warning, null, args, null, (s, e) => string.Format(message, s));
        }

        public static void Warn(this ILogger logger, Exception e, string message, params object[] args)
        {
            logger.Log(LogLevel.Warning, null, args, e, (s, e) => string.Format(message, s));
        }

        public static void Error(this ILogger logger, string message, params object[] args)
        {
            logger.Log(LogLevel.Error, null, args, null, (s, e) => string.Format(message, s));
        }

        public static void Error(this ILogger logger, Exception e, string message, params object[] args)
        {
            logger.Log(LogLevel.Error, null, args, e, (s, e) => string.Format(message, s));
        }

        public static void Fatal(this ILogger logger, string message, params object[] args)
        {
            logger.Log(LogLevel.Critical, null, args, null, (s, e) => string.Format(message, s));
        }

        public static void LogTrace(this ILogger logger, string message, params object[] args)
        {
            logger.Trace(message, args);
        }

        public static void LogInformation(this ILogger logger, string message, params object[] args)
        {
            logger.Info(message, args);
        }

        public static void LogDebug(this ILogger logger, string message, params object[] args)
        {
            logger.Debug(message, args);
        }

        public static void LogWarning(this ILogger logger, string message, params object[] args)
        {
            logger.Warn(message, args);
        }

        public static void LogWarning(this ILogger logger, Exception e, string message, params object[] args)
        {
            logger.Warn(e, message, args);
        }

        public static void LogError(this ILogger logger, string message, params object[] args)
        {
            logger.Error(message, args);
        }

        public static void LogError(this ILogger logger, Exception e, string message, params object[] args)
        {
            logger.Error(e, message, args);
        }

        public static void LogError(this ILogger logger, EventId eventId, Exception e, string message, params object[] args)
        {
            logger.Log(LogLevel.Error, eventId, args, e, (s, e) => string.Format(message, s));
        }

        public static void LogCritical(this ILogger logger, string message, params object[] args)
        {
            logger.Fatal(message, args);
        }

        public static void LogCritical(this ILogger logger, EventId eventId, Exception e, string message, params object[] args)
        {
            logger.Log(LogLevel.Critical, eventId, args, e, (s, e) => string.Format(message, s));
        }
    }
}
