using System;
using System.Diagnostics;

namespace Stratis.Bitcoin.Configuration.Logging
{
    public class LoggerFactory : ILoggerFactory
    {
        public ILogger CreateLogger()
        {
            var methodInfo = new StackTrace().GetFrame(1).GetMethod();
            return new Logger(NLog.LogManager.LogFactory.GetLogger(methodInfo.ReflectedType.ToString()));
        }

        public ILogger CreateLogger(string name)
        {
            return new Logger(NLog.LogManager.LogFactory.GetLogger(name));
        }

        public ILogger CreateLogger(Type type)
        {
            return new Logger(NLog.LogManager.LogFactory.GetLogger(type.FullName));
        }

        public ILogger CreateLogger<T>()
        {
            return new Logger(NLog.LogManager.LogFactory.GetLogger(typeof(T).FullName));
        }

        public void Dispose()
        {
        }
    }
}
