using System;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Stratis.Bitcoin.Configuration.Logging
{
    public static class LogManager
    {
        // Declare the delegate (if using non-generic pattern).
        public delegate void ConfigurationReloadedEventHandler(object sender, NLog.Config.LoggingConfigurationReloadedEventArgs e);

        // Declare the event.
        public static event ConfigurationReloadedEventHandler ConfigurationReloaded;

        static LogManager()
        {
            NLog.LogManager.ConfigurationReloaded += LogManager_ConfigurationReloaded;
        }

        private static void LogManager_ConfigurationReloaded(object sender, NLog.Config.LoggingConfigurationReloadedEventArgs e)
        {
            // Raise the event in a thread-safe manner using the ?. operator.
            ConfigurationReloaded?.Invoke(sender, e);
        }

        public static ILogger GetCurrentClassLogger()
        {
            try
            {
                var stackTrace = new StackTrace();
                if (stackTrace.FrameCount > 1)
                {

                    MethodBase methodInfo = stackTrace.GetFrame(1).GetMethod();
                    return new Logger(NLog.LogManager.GetLogger(methodInfo.ReflectedType.ToString()));
                }
            }
            catch (Exception)
            {
            }

            return new Logger();
        }

        public static void ReconfigExistingLoggers()
        {
            NLog.LogManager.ReconfigExistingLoggers();
        }

        public static NLog.Config.LoggingConfiguration Configuration
        {
            get
            {
                return NLog.LogManager.Configuration;
            }

            set
            {
                NLog.LogManager.Configuration = value;
            }
        }
    }
}
