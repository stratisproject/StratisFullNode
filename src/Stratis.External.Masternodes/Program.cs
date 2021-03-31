using System;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

namespace Stratis.External.Masternodes
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Welcome to the Stratis Masternode Registration application.");
            Console.WriteLine("Please press any key to start.");
            Console.ReadKey();

            var service = new RegistrationService();

            NetworkType networkType = NetworkType.Mainnet;

            if (args.Contains("-testnet"))
                networkType = NetworkType.Testnet;

            if (args.Contains("-regtest"))
                networkType = NetworkType.Regtest;

            await service.StartAsync(networkType);
        }
    }
}
