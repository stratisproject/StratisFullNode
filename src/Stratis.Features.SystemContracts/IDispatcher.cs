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
        Result Dispatch(ISystemContractTransactionContext context);
        uint160 Identifier { get; }
    }
}