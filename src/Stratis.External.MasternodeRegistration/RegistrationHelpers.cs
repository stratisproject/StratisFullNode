using System;
using System.Threading;
using System.Threading.Tasks;

namespace Stratis.External.MasternodeRegistration
{
    public static class RegistrationHelpers
    {
        public static void WaitLoop(Func<bool> act, CancellationToken cancellationToken)
        {
            while (!act())
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Thread.Sleep(TimeSpan.FromSeconds(1));
                }
                catch (OperationCanceledException e)
                {
                    throw e;
                }
            }
        }

        public static async void WaitLoopAsync(Func<Task<bool>> act, CancellationToken cancellationToken)
        {
            while (!await act())
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Thread.Sleep(TimeSpan.FromSeconds(1));
                }
                catch (OperationCanceledException e)
                {
                    throw e;
                }
            }
        }
    }
}
