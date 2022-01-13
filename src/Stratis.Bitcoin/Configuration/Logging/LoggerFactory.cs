using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Stratis.Bitcoin.Configuration.Logging
{
    public class CustomLoggerFactory : ILoggerFactory
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

        public void AddProvider(ILoggerProvider loggerProvider)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
        }
    }
}
