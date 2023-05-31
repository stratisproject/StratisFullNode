using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Stratis.Bitcoin.Features.RPC.Models;
using Stratis.Bitcoin.Utilities.JsonConverters;

namespace Stratis.Bitcoin.Features.RPC.ModelBinders
{
    public class CreateRawTransactionInputBinder : IModelBinder, IModelBinderProvider
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext.ModelType != typeof(CreateRawTransactionInput[]))
            {
                return Task.CompletedTask;
            }

            ValueProviderResult val = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);

            string key = val.FirstValue;

            if (key == null)
            {
                return Task.CompletedTask;
            }

            CreateRawTransactionInput[] inputs = Serializer.ToObject<CreateRawTransactionInput[]>(key);

            bindingContext.Result = ModelBindingResult.Success(inputs);

            return Task.CompletedTask;
        }

        public IModelBinder GetBinder(ModelBinderProviderContext context)
        {
            if (context.Metadata.ModelType == typeof(CreateRawTransactionInput[]))
                return this;

            return null;
        }
    }
}
