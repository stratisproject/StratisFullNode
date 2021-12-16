using System;
using System.Diagnostics;

namespace Stratis.Bitcoin.Configuration.Logging
{
    public class LoggerFactory : ILoggerFactory
    {
        public ILogger CreateLogger()
        {
            var methodInfo = new StackTrace().GetFrame(1).GetMethod();
            return new Logger(NLog.LogManager.LogFactory.GetCurrentClassLogger(), methodInfo.ReflectedType.ToString());
        }

        public ILogger CreateLogger(string name)
        {
            return new Logger(NLog.LogManager.LogFactory.GetCurrentClassLogger(), name);
        }

        public ILogger CreateLogger(Type type)
        {
            return new Logger(NLog.LogManager.LogFactory.GetCurrentClassLogger(), type.FullName);
        }

        public ILogger CreateLogger<T>()
        {
            return new Logger(NLog.LogManager.LogFactory.GetCurrentClassLogger(), typeof(T).FullName);
        }

        public void Dispose()
        {
        }
    }
}
