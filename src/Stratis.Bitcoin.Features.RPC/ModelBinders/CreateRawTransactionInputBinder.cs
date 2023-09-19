using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Stratis.Bitcoin.Features.RPC.Models;
using Stratis.Bitcoin.Utilities.JsonConverters;

namespace Stratis.Bitcoin.Features.RPC.ModelBinders
{
    /// <summary>
    /// Converts the 'inputs' parameter from an incoming createrawtransaction RPC request into a more useful array of <see cref="CreateRawTransactionInput"/>.
    /// <remarks>This is further necessary because the RPC controller cannot handle complex models by default (e.g. <see cref="FundRawTransactionOptions"/>).</remarks>
    /// </summary>
    public class CreateRawTransactionInputBinder : IModelBinder, IModelBinderProvider
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext.ModelType != typeof(CreateRawTransactionInput[]))
            {
                return Task.CompletedTask;
            }

            ValueProviderResult val = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);

            /* Structure of the incoming JSON:
                [
                  {
                    "txid": "hex",        (string, required) The transaction id
                    "vout": n,            (numeric, required) The output number
                    "sequence": n,        (numeric, optional, default=depends on the value of the 'replaceable' and 'locktime' arguments) The sequence number
                  },
                  ...
                ]
            */

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
