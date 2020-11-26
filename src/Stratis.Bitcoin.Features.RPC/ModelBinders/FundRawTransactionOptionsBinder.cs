using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Newtonsoft.Json;

namespace Stratis.Bitcoin.Features.RPC.ModelBinders
{
    public class FundRawTransactionOptionsBinder : IModelBinder, IModelBinderProvider
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext.ModelType != typeof(FundRawTransactionOptions))
            {
                return Task.CompletedTask;
            }

            ValueProviderResult val = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);

            string key = val.FirstValue;
            if (key == null)
            {
                return Task.CompletedTask;
            }

            var options = JsonConvert.DeserializeObject<FundRawTransactionOptions>(key);

            bindingContext.Result = ModelBindingResult.Success(options);
            return Task.CompletedTask;
        }

        public IModelBinder GetBinder(ModelBinderProviderContext context)
        {
            if (context.Metadata.ModelType == typeof(FundRawTransactionOptions))
                return this;
            return null;
        }
    }
}
