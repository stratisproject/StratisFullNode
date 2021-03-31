using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.SmartContracts.PoA;
using Stratis.Bitcoin.Networks;
using Stratis.Sidechains.Networks;

namespace FederationSetup
{
    /// <summary>
    /// The Stratis Federation set-up is a console app that can be sent to Federation Members
    /// in order to set-up the network and generate their Private (and Public) keys without a need to run a Node at this stage.
    /// See the "Use Case - Generate Federation Member Key Pairs" located in the Requirements folder in the project repository.
    /// </summary>
    class Program
    {
        private const string SwitchMineGenesisBlock = "g";
        private const string SwitchGenerateFedPublicPrivateKeys = "p";
        private const string SwitchGenerateMultiSigAddresses = "m";
        private const string SwitchMenu = "menu";
        private const string SwitchExit = "exit";

        private static TextFileConfiguration ConfigReader;

        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                SwitchCommand(args, args[0], string.Join(" ", args));
                return;
            }

            Console.SetIn(new StreamReader(Console.OpenStandardInput(), Console.InputEncoding, false, bufferSize: 1024));

            // Start with the banner and the help message.
            FederationSetup.OutputHeader();
            FederationSetup.OutputMenu();

            while (true)
            {
                try
                {
                    Console.Write("Your choice: ");
                    string userInput = Console.ReadLine().Trim();

                    string command = null;
                    if (!string.IsNullOrEmpty(userInput))
                    {
                        args = userInput.Split(" ");
                        command = args[0];
                    }
                    else
                    {
                        args = null;
                    }

                    Console.WriteLine();

                    SwitchCommand(args, command, userInput);
                }
                catch (Exception ex)
                {
                    FederationSetup.OutputErrorLine($"An error occurred: {ex.Message}");
                    Console.WriteLine();
                    FederationSetup.OutputMenu();
                }
            }
        }

        private static void SwitchCommand(string[] args, string command, string userInput)
        {
            switch (command)
            {
                case SwitchExit:
                    {
                        Environment.Exit(0);
                        break;
                    }
                case SwitchMenu:
                    {
                        HandleSwitchMenuCommand(args);
                        break;
                    }
                case SwitchMineGenesisBlock:
                    {
                        HandleSwitchMineGenesisBlockCommand(userInput);
                        break;
                    }
                case SwitchGenerateFedPublicPrivateKeys:
                    {
                        HandleSwitchGenerateFedPublicPrivateKeysCommand(args);
                        break;
                    }
                case SwitchGenerateMultiSigAddresses:
                    {
                        HandleSwitchGenerateMultiSigAddressesCommand(args);
                        break;
                    }
            }
        }

        private static void HandleSwitchMenuCommand(string[] args)
        {
            if (args.Length != 1)
                throw new ArgumentException("Please enter the exact number of argument required.");

            FederationSetup.OutputMenu();
        }

        private static void HandleSwitchMineGenesisBlockCommand(string userInput)
        {
            int index = userInput.IndexOf("text=");
            if (index < 0)
                throw new ArgumentException("The -text=\"<text>\" argument is missing.");

            string text = userInput.Substring(userInput.IndexOf("text=") + 5);

            if (text.Substring(0, 1) != "\"" || text.Substring(text.Length - 1, 1) != "\"")
                throw new ArgumentException("The -text=\"<text>\" argument should have double-quotes.");

            text = text.Substring(1, text.Length - 2);

            if (string.IsNullOrEmpty(text))
                throw new ArgumentException("Please specify the text to be included in the genesis block.");

            Console.WriteLine(new GenesisMiner().MineGenesisBlocks(new SmartContractPoAConsensusFactory(), text));
            FederationSetup.OutputSuccess();
        }

        private static void HandleSwitchGenerateFedPublicPrivateKeysCommand(string[] args)
        {
            if (args.Length != 1 && args.Length != 2 && args.Length != 3 && args.Length != 4)
                throw new ArgumentException("Please enter the exact number of argument required.");

            string passphrase = null;
            string dataDirPath = null;
            string isMultisig = null;

            dataDirPath = Array.Find(args, element =>
                element.StartsWith("-datadir=", StringComparison.Ordinal));

            passphrase = Array.Find(args, element =>
                element.StartsWith("-passphrase=", StringComparison.Ordinal));

            isMultisig = Array.Find(args, element =>
                element.StartsWith("-ismultisig=", StringComparison.Ordinal));

            if (string.IsNullOrEmpty(passphrase))
                throw new ArgumentException("The -passphrase=\"<passphrase>\" argument is missing.");

            passphrase = passphrase.Replace("-passphrase=", string.Empty);

            //ToDo wont allow for datadir with equal sign
            dataDirPath = string.IsNullOrEmpty(dataDirPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : dataDirPath.Replace("-datadir=", string.Empty);

            if (string.IsNullOrEmpty(isMultisig) || isMultisig.Replace("-ismultisig=", string.Empty) == "true")
            {
                GeneratePublicPrivateKeys(passphrase, dataDirPath);
            }
            else
            {
                GeneratePublicPrivateKeys(passphrase, dataDirPath, isMultiSigOutput: false);
            }

            FederationSetup.OutputSuccess();
        }

        private static void ConfirmArguments(TextFileConfiguration config, params string[] args)
        {
            var missing = new Dictionary<string, string>();

            foreach (string arg in args)
            {
                if (config.GetOrDefault<string>(arg, null) == null)
                {
                    Console.Write(arg + ": ");
                    missing[arg] = Console.ReadLine();
                }
            }

            new TextFileConfiguration(missing.Select(d => $"{d.Key}={d.Value}").ToArray()).MergeInto(config);

            Console.WriteLine();
        }

        private static void HandleSwitchGenerateMultiSigAddressesCommand(string[] args)
        {
            ConfigReader = new TextFileConfiguration(args);

            ConfirmArguments(ConfigReader, "network");

            (_, Network sideChain, Network targetMainChain) = GetMainAndSideChainNetworksFromArguments();

            Console.WriteLine($"Creating multisig addresses for {targetMainChain.Name} and {sideChain.Name}.");
            Console.WriteLine(new MultisigAddressCreator().CreateMultisigAddresses(targetMainChain, sideChain));
        }

        private static void GeneratePublicPrivateKeys(string passphrase, string keyPath, bool isMultiSigOutput = true)
        {
            // Generate keys for signing.
            var mnemonicForSigningKey = new Mnemonic(Wordlist.English, WordCount.Twelve);
            PubKey signingPubKey = mnemonicForSigningKey.DeriveExtKey(passphrase).PrivateKey.PubKey;

            // Generate keys for migning.
            var tool = new KeyTool(keyPath);

            Key key = tool.GeneratePrivateKey();

            string savePath = tool.GetPrivateKeySavePath();
            tool.SavePrivateKey(key);
            PubKey miningPubKey = key.PubKey;

            Console.WriteLine($"Your Masternode Public Key: {Encoders.Hex.EncodeData(miningPubKey.ToBytes(false))}");
            Console.WriteLine($"-----------------------------------------------------------------------------");

            if (isMultiSigOutput)
            {
                Console.WriteLine(
                    $"Your Masternode Signing Key: {Encoders.Hex.EncodeData(signingPubKey.ToBytes(false))}");
                Console.WriteLine(Environment.NewLine);
                Console.WriteLine(
                    $"------------------------------------------------------------------------------------------");
                Console.WriteLine(
                    $"-- Please keep the following 12 words for yourself and note them down in a secure place --");
                Console.WriteLine(
                    $"------------------------------------------------------------------------------------------");
                Console.WriteLine($"Your signing mnemonic: {string.Join(" ", mnemonicForSigningKey.Words)}");
            }

            if (passphrase != null)
            {
                Console.WriteLine(Environment.NewLine);
                Console.WriteLine($"Your passphrase: {passphrase}");
            }

            Console.WriteLine(Environment.NewLine);
            Console.WriteLine($"------------------------------------------------------------------------------------------------------------");
            Console.WriteLine($"-- Please save the following file in a secure place, you'll need it when the federation has been created. --");
            Console.WriteLine($"------------------------------------------------------------------------------------------------------------");
            Console.WriteLine($"File path: {savePath}");
            Console.WriteLine(Environment.NewLine);
        }

        private static (Network mainChain, Network sideChain, Network targetMainChain) GetMainAndSideChainNetworksFromArguments()
        {
            string network = ConfigReader.GetOrDefault("network", (string)null);

            if (string.IsNullOrEmpty(network))
                throw new ArgumentException("Please specify a network.");

            Network mainchainNetwork, sideChainNetwork, targetMainchainNetwork;
            switch (network)
            {
                case "mainnet":
                    mainchainNetwork = null;
                    sideChainNetwork = CirrusNetwork.NetworksSelector.Mainnet();
                    targetMainchainNetwork = Networks.Strax.Mainnet();
                    break;
                case "testnet":
                    mainchainNetwork = null;
                    sideChainNetwork = CirrusNetwork.NetworksSelector.Testnet();
                    targetMainchainNetwork = Networks.Strax.Testnet();
                    break;
                case "regtest":
                    mainchainNetwork = null;
                    sideChainNetwork = CirrusNetwork.NetworksSelector.Regtest();
                    targetMainchainNetwork = Networks.Strax.Regtest();
                    break;
                default:
                    throw new ArgumentException("Please specify a network such as: mainnet, testnet or regtest.");

            }

            return (mainchainNetwork, sideChainNetwork, targetMainchainNetwork);
        }
    }
}
