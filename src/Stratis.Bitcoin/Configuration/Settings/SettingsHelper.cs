using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace Stratis.Bitcoin.Configuration.Settings
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class CommandLineOptionAttribute : Attribute
    {
        public string Option { get; private set; }

        public string Description { get; private set; }

        public CommandLineOptionAttribute(string option, string description)
        {
            this.Option = option;
            this.Description = description;
        }
    }

    public static class SettingsHelper
    {
        private static string TypeDescription(Type type)
        {
            if (type == typeof(bool))
                return "0 or 1";

            if (type == typeof(int))
                return "number";

            return type.ToString().Split('.').Last().ToLower();
        }

        private static string DefaultValue(object value, bool raw = false)
        {
            if (value == null)
                return string.Empty;

            if (value.GetType() == typeof(bool))
                value = ((bool)value) ? "1" : "0";

            return raw ? value.ToString() : $" Default: {value}.";
        }

        /// <summary>
        /// Displays wallet configuration help information on the console.
        /// </summary>
        /// <param name="settingsType">The settings class type.</param>
        /// <param name="network">The network.</param>
        public static void PrintHelp(Type settingsType, Network network)
        {
            NodeSettings defaultSettings = NodeSettings.Default(network);
            var builder = new StringBuilder();
            var defaultValues = Activator.CreateInstance(settingsType, new object[] { defaultSettings });

            foreach (PropertyInfo pi in settingsType.GetProperties())
            {
                CommandLineOptionAttribute attr = Attribute.GetCustomAttributes(pi).OfType<CommandLineOptionAttribute>().FirstOrDefault();
                if (attr == null)
                    continue;

                object defaultValue = pi.GetValue(defaultValues);

                string option = $"{attr.Option}=<{TypeDescription(pi.PropertyType)}>";

                builder.AppendLine($"-{option.PadRight(29)} {attr.Description}{DefaultValue(defaultValue)}");
            }

            var logger = defaultSettings.LoggerFactory.CreateLogger(settingsType.FullName);
            logger.LogInformation(builder.ToString());
        }

        /// <summary>
        /// Get the default configuration.
        /// </summary>
        /// <param name="settingsType">The settings class type.</param>
        /// <param name="builder">The string builder to add the settings to.</param>
        /// <param name="network">The network to base the defaults off.</param>
        public static void BuildDefaultConfigurationFile(Type settingsType, StringBuilder builder, Network network)
        {
            builder.AppendLine($"####{settingsType.FullName}####");

            NodeSettings defaultSettings = NodeSettings.Default(network);
            var defaultValues = Activator.CreateInstance(settingsType, new object[] { defaultSettings });

            foreach (PropertyInfo pi in settingsType.GetProperties())
            {
                CommandLineOptionAttribute attr = Attribute.GetCustomAttributes(pi).OfType<CommandLineOptionAttribute>().FirstOrDefault();
                if (attr == null)
                    continue;

                object defaultValue = pi.GetValue(defaultValues);

                string option = $"{attr.Option}={DefaultValue(defaultValue, true)}";

                builder.AppendLine($"#{attr.Description}");
                builder.AppendLine($"#{option}");
            }
        }
    }
}
