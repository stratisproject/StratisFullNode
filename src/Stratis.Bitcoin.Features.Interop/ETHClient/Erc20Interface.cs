using System.Numerics;
using System.Threading.Tasks;
using Nethereum.Contracts.ContractHandlers;
using Nethereum.Web3;

namespace Stratis.Bitcoin.Features.Interop.ETHClient
{
    public class Erc20Interface
    {
        public static async Task<BigInteger> GetBalanceAsync(Web3 web3, string contractAddress, string addressToQuery)
        {
            var balanceOfFunctionMessage = new BalanceOfFunction()
            {
                Owner = addressToQuery
            };

            IContractQueryHandler<BalanceOfFunction> balanceHandler = web3.Eth.GetContractQueryHandler<BalanceOfFunction>();
            BigInteger balance = await balanceHandler.QueryAsync<BigInteger>(contractAddress, balanceOfFunctionMessage);

            return balance;
        }
    }
}
