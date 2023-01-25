using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Newtonsoft.Json.Linq;
using Stratis.Bitcoin.Features.RPC.Models;

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

        JArray outerArray = JArray.Parse(raw);

        var model = new List<CreateRawTransactionOutput>();

        foreach (var item in outerArray.Children<JObject>())
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
