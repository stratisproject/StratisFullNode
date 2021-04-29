using CSharpFunctionalExtensions;
using NBitcoin;

namespace Stratis.Features.SystemContracts
{
    public interface IDispatcher<T> : IDispatcher
    {
        T GetInstance(ISystemContractTransactionContext context);
    }

    public interface IDispatcher
    {
        Result<object> Dispatch(ISystemContractTransactionContext context);
        uint160 Identifier { get; }
    }

    public static class DispatchResult
    {
        /// <summary>
        /// C# functional extensions doesn't support returning nulls with a Result<T> class, so we use
        /// <see cref="Void"/> to return from void methods.
        /// </summary>
        public static object Void = new object();
    }
}