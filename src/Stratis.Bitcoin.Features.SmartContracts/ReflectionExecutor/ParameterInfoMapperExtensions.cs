﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using Stratis.SmartContracts.CLR.Serialization;

namespace Stratis.Bitcoin.Features.SmartContracts.ReflectionExecutor
{
    public static class ParameterInfoMapperExtensions
    {
        /// <summary>
        /// Maps a JObject of values to the parameters on a method.
        /// </summary>
        public static string[] Map(this ParameterInfo[] parameters, JObject obj)
        {
            var result = new List<string>();

            foreach (ParameterInfo parameter in parameters)
            {
                JToken jObParam = obj[parameter.Name];

                if (jObParam == null)
                    throw new Exception("Couldn't map all params");

                Prefix prefix = Prefix.ForType(parameter.ParameterType);

                result.Add($"{prefix.Value}#{jObParam}");
            }

            return result.ToArray();
        }
    }
}
