using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.SmartContracts.Interop.EthereumClient;
using Stratis.Bitcoin.Features.SmartContracts.Interop.Models;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Persistence;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Utilities;
using Stratis.SmartContracts.CLR.Serialization;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Bitcoin.Features.SmartContracts.Interop
{
    public class InteropPoller : IDisposable
    {
        private const string LastScannedHeightKey = "InteropLastScannedHeight";
        private const int InteropBatchSize = 100;

        private const string ContractAddressKeyPrefix = "CN";
        private const string MethodNameKeyPrefix = "MN";
        private const string ParameterKeyPrefix = "P";
        private const string ParameterCountMarker = "X";

        private readonly InteropSettings interopSettings;
        private readonly IEthereumClientBase ethereumClientBase;
        private readonly Network network;
        private readonly IAsyncProvider asyncProvider;
        private readonly INodeLifetime nodeLifetime;
        private readonly ChainIndexer chainIndexer;
        private readonly IConsensusManager consensusManager;
        private readonly ILogger logger;
        private readonly IContractPrimitiveSerializer primitiveSerializer;
        private readonly IStateRepositoryRoot stateRoot;
        private readonly IInitialBlockDownloadState initialBlockDownloadState;
        private readonly IFederationManager federationManager;
        private readonly IFederationHistory federationHistory;
        private readonly IInteropRequestRepository interopRequestRepository;
        private readonly IBlockStoreQueue blockStoreQueue;
        private readonly IKeyValueRepository keyValueRepo;
        private readonly IConversionRequestRepository conversionRequestRepository;
        private readonly IInteropTransactionManager interopTransactionManager;

        private IAsyncLoop interopLoop;
        private IAsyncLoop conversionLoop;

        //private readonly ApiLogDeserializer deserializer;

        private ChainedHeader lastScanned;

        public InteropPoller(NodeSettings nodeSettings,
            InteropSettings interopSettings,
            IEthereumClientBase ethereumClientBase,
            IAsyncProvider asyncProvider,
            INodeLifetime nodeLifetime,
            ChainIndexer chainIndexer,
            IConsensusManager consensusManager,
            IContractPrimitiveSerializer primitiveSerializer,
            IStateRepositoryRoot stateRoot,
            IInitialBlockDownloadState initialBlockDownloadState,
            IFederationManager federationManager,
            IFederationHistory federationHistory,
            //IInteropRequestRepository interopRequestRepository,
            IBlockStoreQueue blockStoreQueue,
            IKeyValueRepository keyValueRepo,
            IConversionRequestRepository conversionRequestRepository,
            IInteropTransactionManager interopTransactionManager)
        {
            this.interopSettings = interopSettings;
            this.ethereumClientBase = ethereumClientBase;
            this.network = nodeSettings.Network;
            this.asyncProvider = asyncProvider;
            this.nodeLifetime = nodeLifetime;
            this.chainIndexer = chainIndexer;
            this.consensusManager = consensusManager;
            this.primitiveSerializer = primitiveSerializer;
            this.stateRoot = stateRoot;
            this.initialBlockDownloadState = initialBlockDownloadState;
            this.federationManager = federationManager;
            this.federationHistory = federationHistory;
            //this.interopRequestRepository = interopRequestRepository;
            this.blockStoreQueue = blockStoreQueue;
            this.keyValueRepo = keyValueRepo;
            this.conversionRequestRepository = conversionRequestRepository;
            this.interopTransactionManager = interopTransactionManager;
            this.logger = nodeSettings.LoggerFactory.CreateLogger(this.GetType().FullName);
            
            this.lastScanned = this.chainIndexer.Genesis;
        }

        public void Initialize()
        {
            if (!this.interopSettings.Enabled)
                return;

            if (!this.federationManager.IsFederationMember)
                return;

            this.logger.LogDebug($"Interoperability enabled, initializing periodic loop.");

            HashHeightPair lastScannedHashHeight = this.keyValueRepo.LoadValue<HashHeightPair>(LastScannedHeightKey);

            if (lastScannedHashHeight != null)
                this.lastScanned = this.chainIndexer.Tip.FindAncestorOrSelf(lastScannedHashHeight.Hash, lastScannedHashHeight.Height);
            else
                // Genesis.
                this.lastScanned = this.chainIndexer.Tip.GetAncestor(0);

            this.logger.LogDebug($"Interoperability last scanned height set to {this.lastScanned.Height}.");

            // Initialize the interop polling loop, to check for interop contract requests.
            this.interopLoop = this.asyncProvider.CreateAndRunAsyncLoop("PeriodicCheckInterop", async (cancellation) =>
                {
                    if (this.initialBlockDownloadState.IsInitialBlockDownload())
                        return;

                    this.logger.LogTrace("Beginning interop loop.");

                    try
                    {
                        this.CheckForEthereumRequests();
                        this.ProcessEthereumRequests();

                        this.CheckForStratisRequests();
                        this.ProcessStratisRequests();
                        this.TransmitStratisResponses();
                    }
                    catch (Exception e)
                    {
                        this.logger.LogWarning($"Exception raised when checking interop requests. {e}");
                    }

                    this.logger.LogTrace("Finishing interop loop.");
                },
                this.nodeLifetime.ApplicationStopping,
                repeatEvery: TimeSpans.TenSeconds,
                startAfter: TimeSpans.Second);

            // Initialize the conversion polling loop, to check for conversion requests.
            this.conversionLoop = this.asyncProvider.CreateAndRunAsyncLoop("PeriodicCheckConversionStore", async (cancellation) =>
                {
                    if (this.initialBlockDownloadState.IsInitialBlockDownload())
                        return;

                    this.logger.LogTrace("Beginning conversion processing loop.");

                    try
                    {
                        this.ProcessConversionRequests();
                    }
                    catch (Exception e)
                    {
                        this.logger.LogWarning($"Exception raised when checking conversion requests. {e}");
                    }

                    this.logger.LogTrace("Finishing conversion processing loop.");
                },
                this.nodeLifetime.ApplicationStopping,
                repeatEvery: TimeSpans.TenSeconds,
                startAfter: TimeSpans.Second);
        }

        private void ProcessConversionRequests()
        {
            List<ConversionRequest> mintRequests = this.conversionRequestRepository.GetAllMint(true);

            foreach (ConversionRequest request in mintRequests)
            {
                IFederationMember member = this.federationHistory.GetFederationMemberForBlock(this.chainIndexer.Tip);

                bool originator = member.Equals(this.federationManager.GetCurrentFederationMember());

                // If this node is the designated transaction originator, it must create and submit the transaction to the multisig.
                if (originator)
                {
                    // First construct the necessary minting transaction data, utilising the ABI of the wrapped STRAX ERC20 contract.
                    string abiData = this.ethereumClientBase.EncodeMintParams(request.DestinationAddress, request.Amount);

                    this.ethereumClientBase.SubmitTransaction(request.DestinationAddress, 0, abiData);

                    // It must then propagate the transactionId to the other nodes so that they know they should confirm it.
                    // The reason why each node doesn't simply maintain its own transaction counter, is that it can't be guaranteed
                    // that a transaction won't be submitted out-of-turn by a rogue or malfunctioning federation node.
                    // The coordination mechanism safeguards against this, as any such spurious transaction will not receive acceptance votes.
                    // The transactionId must be accompanied by the hash of the submission transaction on the Ethereum chain so that it can be verified.

                    // The originator isn't responsible for anything further at this point.
                    request.RequestStatus = (int)ConversionRequestStatus.Processed;
                    request.Processed = true;

                    this.conversionRequestRepository.Save(request);
                }
                else
                {
                    // If not the originator, this node needs to determine what multisig wallet transactionId it should confirm.
                    // Initially there will not be a quorum of nodes that agree on the transactionId.
                    // So each node needs to satisfy itself that the transactionId sent by the originator exists in the multisig wallet.
                    // This is done within the InteropBehavior automatically, we just check each poll loop if a transaction has enough votes yet.
                    int quorum = this.network.Federations.GetOnlyFederation().GetFederationDetails().signaturesRequired;

                    // Each node must only ever confirm a single transactionId for a given conversion transaction.
                    BigInteger agreedUponId = this.interopTransactionManager.GetAgreedTransactionId(request.RequestId, quorum);

                    if (agreedUponId != BigInteger.MinusOne)
                    {
                        // Once a quorum is reached, each node confirms the agreed transactionId.
                        // If the originator or some other nodes renege on their vote, the current node will not re-confirm a different transactionId.
                        this.ethereumClientBase.ConfirmTransaction(agreedUponId);

                        request.RequestStatus = (int)ConversionRequestStatus.Processed; // TODO: .Confirmed?
                        request.Processed = true;

                        this.conversionRequestRepository.Save(request);
                    }
                }
            }

            List<ConversionRequest> burnRequests = this.conversionRequestRepository.GetAllBurn(true);

            foreach (ConversionRequest burn in burnRequests)
            {
                // Unlike the mint requests, burns are not initiated by the multisig wallet.

                // Properly processing burn transactions requires emulating a withdrawal from the Cirrus chain.
                // It will be easier when conversion can be done directly to and from a Cirrus contract.
            }
        }

        private void CheckForEthereumRequests()
        {
            if (string.IsNullOrWhiteSpace(this.interopSettings.InteropContractCirrusAddress))
                return;

            // TODO: Is reorg recovery needed? In any case we can't really undo a request from the other chain if we've returned a response to it already
            IEnumerable<ChainedHeader> blockHeaders = this.chainIndexer.EnumerateToTip(this.lastScanned);
            
            // As endorsements don't have receipts at present we can't filter out the blocks in any meaningful way. So just loop through all of them.
            ChainedHeader scanned = this.lastScanned;

            int count = 0;

            foreach (ChainedHeader chainedHeader in blockHeaders)
            {
                if (count == InteropBatchSize)
                    break;

                Block block;

                if (chainedHeader.Block != null)
                {
                    block = chainedHeader.Block;
                }
                else
                {
                    ChainedHeaderBlock res = this.consensusManager.GetBlockData(chainedHeader.HashBlock);

                    if (res?.Block == null)
                    {
                        // If the block still can't be found we want to be able to investigate why.
                        // In theory we could use GetOrDownload but that complicates the relationship between this component and consensus.
                        this.logger.LogWarning($"Unable to retrieve block with hash {chainedHeader.HashBlock}. Availability: {chainedHeader.BlockDataAvailability}.");

                        continue;
                    }

                    block = res.Block;
                }

                List<InteropRequest> requests = ExtractInteropRequests(block, this.stateRoot, this.interopSettings.InteropContractCirrusAddress, this.logger);

                foreach (InteropRequest request in requests)
                {
                    this.interopRequestRepository.Save(request);
                }

                count++;
            }

            this.lastScanned = scanned;
            var lastScannedHashHeight = new HashHeightPair(this.lastScanned);
            this.keyValueRepo.SaveValue<HashHeightPair>(LastScannedHeightKey, lastScannedHashHeight);
        }

        public static List<InteropRequest> ExtractInteropRequests(Block block, IStateRepositoryRoot stateRoot, string interopContractAddress, ILogger logger)
        {
            var requests = new List<InteropRequest>();
            /*
            // For this block, get all applicable receipts and check if they are interop requests.

            foreach (Transaction transaction in block.Transactions)
            {
                // First check if we can get a receipt for this transaction.
                // If not, it can't possibly be an interop request.

                if (receipt == null)
                    continue;

                // Our primary identification for whether the receipt is for an interop request, is if it involves an interop contract at all.
                // So we check the address first as a quick filter.
                if (receipt.ContractAddress != uint160.Parse(interopContractAddress))
                    continue;

                logger.LogTrace($"Found a receipt for interop contract address {receipt.ContractAddress}.");

                InteropRequest request = null;
                var parameters = new List<string>();

                // We set this empty to keep the compiler happy more than anything else.
                string requestGuid = "";

                // This is a bit kludgy, but we need the following information from the interop call:
                // - chaincode name
                // - method name
                // - parameter values
                // We get this by using a particular format for the keys in the writes.
                // To keep the keys globally unique we prefix them with a type marker and request guid:
                // Key: CN<GUID> Value: <chainCodeNameString>
                // Key: MN<GUID> Value: <methodNameString>
                // Key: PnnnX<GUID> Value: <parameterValue> -> there isn't a prescribed maximum number of parameters currently; so X is used as a termination character for the param sequence number
                foreach (var log in receipt)
                {
                    string key = Encoding.UTF8.GetString(write.Key);
                    string value = Encoding.UTF8.GetString(write.Value);

                    if (key.Length < (2 + 36)) // Shortest possible prefix + GUID
                        break;

                    if (!(key.StartsWith(ContractAddressKeyPrefix) || key.StartsWith(MethodNameKeyPrefix) || key.StartsWith(ParameterKeyPrefix)))
                        break;

                    if (key.StartsWith(ContractAddressKeyPrefix))
                    {
                        if (request != null)
                            break;

                        requestGuid = key.Substring(2, 36);

                        if (string.IsNullOrWhiteSpace(requestGuid))
                            break;

                        request = new InteropRequest()
                        {
                            RequestId = requestGuid,
                            TransactionId = transaction.GetHash().ToString(),
                            RequestType = (int)InteropRequestType.InvokeEthereum,
                            ContractAddress = receipt.ContractAddress.ToString(),
                            // The chaincode name, method name and parameters get added to this separately so they start out empty.
                            ChaincodeName = value,
                            MethodName = "",
                            Parameters = new string[] { },
                            Processed = false,
                            Response = ""
                        };
                    }

                    if (key.StartsWith(MethodNameKeyPrefix))
                    {
                        if (request == null)
                            break;

                        // Validate the GUID to check that it is consistent across all the logs.
                        string guid = key.Substring(2, 36);

                        if (!requestGuid.Equals(guid))
                            break;

                        request.MethodName = value;
                    }

                    if (key.StartsWith(ParameterKeyPrefix))
                    {
                        if (request == null)
                            break;

                        // Find the position of the first 'X' character
                        int pos = key.IndexOf(ParameterCountMarker);

                        if (pos == -1)
                            break;

                        // Validate the GUID to check that it is consistent across all the logs.
                        string guid = string.Concat(key.Skip(pos + 1).Take(36));

                        if (!requestGuid.Equals(guid))
                            break;

                        logger.LogTrace($"Found an interop request parameter with value '{value}'.");

                        parameters.Add(value);
                    }
                }

                if ((request == null) || string.IsNullOrWhiteSpace(request.ContractAddress) || string.IsNullOrWhiteSpace(request.MethodName))
                {
                    logger.LogDebug($"Transaction {transaction.GetHash()} did not contain a valid interop request, ignoring it.");

                    continue;
                }

                request.Parameters = parameters.ToArray();
                requests.Add(request);

                logger.LogDebug($"Found interop request {request.RequestId} in block {block.GetHash()}.");
            }

            logger.LogDebug($"Found {requests.Count} interop requests.");
            */
            return requests;
        }

        private void ProcessEthereumRequests()
        {
            if (this.interopRequestRepository == null)
                return;

            List<InteropRequest> requests = this.interopRequestRepository.GetAllEthereum(true);

            foreach (InteropRequest request in requests)
            {
                /*
                Dictionary<string, string> responseDict = this.client.InvokeContract(request);

                // Store response from the client into the interop contract persistent store.
                this.StoreEthereumResponse(request, responseDict["result"]);

                request.Response = responseDict["result"];
                */
                request.Processed = true;

                this.interopRequestRepository.Save(request);
            }
        }

        private void StoreEthereumResponse(InteropRequest request, string response)
        {
            /*
            var callModel = new BuildCallContractTransactionModel()
            {
                // The response needs to be stored against the same interop contract deployment as the request.
                Address = uint160.Parse(request.ContractAddress).ToBase58Address(this.network),
                MethodName = "StoreInteropResult",
                Parameters = new string[] { "4#" + request.RequestId, "4#" + response }
            };

            // Build and send the transaction using the EthereumClient
            */
        }

        private void CheckForStratisRequests()
        {
            List<EthereumRequestModel> requests;

            try
            {
                // Retrieved from the logs of the Ethereum interop contract.
                requests = this.ethereumClientBase.GetStratisInteropRequests();
            }
            catch (Exception e)
            {
                this.logger.LogWarning("Exception during Stratis interop request lookup: " + e);

                requests = new List<EthereumRequestModel>();
            }

            foreach (EthereumRequestModel contractRequest in requests)
            {
                if (this.interopRequestRepository.Get(contractRequest.RequestId) != null)
                {
                    this.logger.LogDebug($"Already saved interop request {contractRequest.RequestId}.");

                    continue;
                }

                this.logger.LogDebug($"Saving interop request {contractRequest.RequestId} from Fabric network.");

                var request = new InteropRequest() { 
                    RequestId = contractRequest.RequestId,
                    TransactionId = "",
                    RequestType = (int)InteropRequestType.InvokeStratis,
                    TargetContractAddress = contractRequest.ContractAddress,
                    SourceAddress = "", // TODO: Need to populate this
                    MethodName = contractRequest.MethodName,
                    Parameters = contractRequest.Parameters.ToArray(),
                    Processed = false,
                    Response = ""
                };

                this.interopRequestRepository.Save(request);
            }
        }

        private void ProcessStratisRequests()
        {
            if (this.interopRequestRepository == null)
                return;

            List<InteropRequest> requests = this.interopRequestRepository.GetAllStratis(true);

            this.logger.LogDebug($"Processing {requests.Count} stored interop requests from Ethereum network.");

            foreach (InteropRequest request in requests)
            {
                if (!string.IsNullOrEmpty(request.TransactionId))
                {
                    // This request has already been processed and we are waiting for the contract call transaction to appear in a block.
                    continue;
                }

                this.logger.LogDebug($"Processing stored interop request {request.RequestId} from Fabric network.");
                
                /*
                // TODO: Area of improvement - check the method signature of the contract being called and translate the parameters to their proper types
                string[] translatedParameters = request.Parameters.Select(a => "4#" + a).ToArray();

                var callModel = new BuildCallContractTransactionModel()
                {
                    Address = request.ContractAddress,
                    MethodName = request.MethodName,
                    Parameters = translatedParameters
                };

                // Build and send the transaction to the Cirrus network.

                // The actual response needs to be transmitted back to the Ethereum network. However, this can only be done with finality when the next block is mined.
                // So we just record the transaction id of the contract call for now so it can be looked up later.
                request.TransactionId = callResponse.TransactionId.ToString();

                this.logger.LogDebug($"Flagging interop request {request.RequestId} as awaiting confirmation.");

                this.interopRequestRepository.Save(request);
                */
            }
        }

        private void TransmitStratisResponses()
        {
            if (this.interopRequestRepository == null)
                return;

            List<InteropRequest> requests = this.interopRequestRepository.GetAllStratis(true);

            foreach (InteropRequest request in requests)
            {
                if (string.IsNullOrEmpty(request.TransactionId))
                {
                    // This request has not been processed yet so we can't check the confirmation status of the contract call transaction.
                    continue;
                }

                uint256 txIdToCheck = uint256.Parse(request.TransactionId);

                /*
                // Check if the contract call transaction is fully confirmed.

                if (this.blockStoreQueue.GetBlockIdByTransactionId(txIdToCheck) == null)
                {
                    this.logger.LogDebug($"The contract call transaction for interop request {request.RequestId} is not confirmed yet.");

                    continue;
                }

                // Get the receipt, and ultimately the logs, from the contract call.
                
                this.logger.LogDebug($"Transmitting interop request result for {request.RequestId} to Ethereum network.");

                try
                {
                    this.client.TransmitResponse(request);
                }
                catch (Exception e)
                {
                    // TODO: If it fails repeatedly we should probably flag the request as processed anyway and move on to prevent it from continually occupying the poller loop
                    this.logger.LogWarning($"Failed to transmit response for request {request.RequestId} to Ethereum network. {e}");

                    continue;
                }

                request.Processed = true;

                this.logger.LogDebug($"Flagging interop request {request.RequestId} as completed.");

                this.interopRequestRepository.Save(request);
                */
            }
        }

        public void Dispose()
        {
            this.interopLoop?.Dispose();
            this.conversionLoop?.Dispose();

            var lastScannedHashHeight = new HashHeightPair(this.lastScanned);
            this.keyValueRepo.SaveValue<HashHeightPair>(LastScannedHeightKey, lastScannedHashHeight);
        }
    }
}
