using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NBitcoin;

namespace Stratis.Bitcoin.Features.RPC.ModelBinders
{
    public class MoneyModelBinder : IModelBinder, IModelBinderProvider
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext.ModelType != typeof(Money))
            {
                return Task.CompletedTask;
            }

            ValueProviderResult val = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);

            string key = val.FirstValue;
            if (key == null)
            {
                return Task.CompletedTask;
            }

            // TODO: Check if this is actually correct. The other binders set the result in the binding context?
            return Task.FromResult(Money.Parse(key));
        }

        public IModelBinder GetBinder(ModelBinderProviderContext context)
        {
            if (context.Metadata.ModelType == typeof(Money))
                return this;

            return null;
        }
    }
}
