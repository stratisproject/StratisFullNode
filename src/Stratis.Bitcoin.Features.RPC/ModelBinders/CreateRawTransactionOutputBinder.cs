using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Newtonsoft.Json.Linq;
using Stratis.Bitcoin.Features.RPC.Models;

/// <summary>
/// Converts the key-value pair list data structure from an incoming createrawtransaction RPC request into a more useful array of <see cref="CreateRawTransactionOutput"/>.
/// </summary>
public class CreateRawTransactionOutputBinder : IModelBinder, IModelBinderProvider
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        if (bindingContext.ModelType != typeof(CreateRawTransactionOutput[]))
        {
            return Task.CompletedTask;
        }

        ValueProviderResult val = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);

        string raw = val.FirstValue;

        if (raw == null)
        {
            return Task.CompletedTask;
        }

        /* The bitcoind implementation of createrawtransaction accepts the following data structure:

                [
                  {                       (json object)
                    "address": amount,    (numeric or string, required) A key-value pair. The key (string) is the bitcoin address, the value (float or string) is the amount in BTC
                  },
                  {                       (json object)
                    "data": "hex",        (string, required) A key-value pair. The key must be "data", the value is hex-encoded data
                  },
                  ...
                ]

        This does not lend itself to conventional JSON models, because the 'address' key can be any valid address string.
        Therefore, we have to manually parse the JSON array and build an array of transaction output objects that will be bound and passed into the controller.
        */

        JArray outerArray = JArray.Parse(raw);

        var model = new List<CreateRawTransactionOutput>();

        foreach (JObject item in outerArray.Children<JObject>())
        {
            foreach (JProperty property in item.Properties())
            {
                string addressOrData = property.Name;
                string value = property.Value.ToString();

                model.Add(new CreateRawTransactionOutput() { Key = addressOrData, Value = value });
            }
        }

        bindingContext.Result = ModelBindingResult.Success(model.ToArray());

        return Task.CompletedTask;
    }

    public IModelBinder GetBinder(ModelBinderProviderContext context)
    {
        if (context.Metadata.ModelType == typeof(CreateRawTransactionOutput[]))
            return this;

        return null;
    }
}
