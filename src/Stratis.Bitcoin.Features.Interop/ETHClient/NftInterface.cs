using System.Numerics;
using System.Threading.Tasks;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Contracts.ContractHandlers;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;

namespace Stratis.Bitcoin.Features.Interop.ETHClient
{
    [Event("Transfer")]
    public class NftTransferEventDTO : IEventDTO
    {
        /// <summary>
        /// The sender of the token.
        /// </summary>
        [Parameter("address", "_from", 1, true)]
        public string From { get; set; }

        /// <summary>
        /// The recipient of the token.
        /// </summary>
        [Parameter("address", "_to", 2, true)]
        public string To { get; set; }

        /// <summary>
        /// The unique identifier of the token that was transferred from the sender to the recipient.
        /// Note that if the sender was the zero address the token was newly minted, and if the recipient
        /// was the zero address then the token is being burned.
        /// </summary>
        /// <remarks>Note that the indexed parameter is true for an ERC721 Transfer event. This is the only difference between it
        /// and the ERC20 equivalent. However, Nethereum requires the DTO to match exactly and we therefore need two classes.</remarks>
        [Parameter("uint256", "_tokenId", 3, true)]
        public BigInteger TokenId { get; set; }
    }

    [Function("tokenURI", "string")]
    public class TokenUriFunction : FunctionMessage
    {
        [Parameter("uint256", "tokenId", 1)]
        public BigInteger TokenId { get; set; }
    }

    [Function("ownerOf", "address")]
    public class NftOwnerFunction : FunctionMessage
    {
        [Parameter("uint256", "tokenId", 1)]
        public BigInteger TokenId { get; set; }
    }

    [Function("safeTransferFrom")]
    public class NftTransferFunction : FunctionMessage
    {
        [Parameter("address", "from", 1)]
        public string From { get; set; }

        [Parameter("address", "to", 2)]
        public string To { get; set; }

        [Parameter("uint256", "tokenId", 3)]
        public BigInteger TokenId { get; set; }
    }

    [Function("mint")]
    public class NftMintFunction : FunctionMessage
    {
        [Parameter("address", "recipient", 1)]
        public string Recipient { get; set; }

        [Parameter("uint256", "tokenId", 2)]
        public BigInteger TokenId { get; set; }

        [Parameter("string", "tokenUri", 3)]
        public string TokenUri { get; set; }
	}

	public class NftInterface
    {
        public static async Task<string> GetTokenOwnerAsync(Web3 web3, string contractAddress, BigInteger tokenId)
        {
            var nftOwnerFunctionMessage = new NftOwnerFunction()
            {
                TokenId = tokenId
            };

            IContractQueryHandler<NftOwnerFunction> ownerHandler = web3.Eth.GetContractQueryHandler<NftOwnerFunction>();
            string owner = await ownerHandler.QueryAsync<string>(contractAddress, nftOwnerFunctionMessage).ConfigureAwait(false);

            return owner;
        }

        /// <summary>
        /// If the NFT contract supports the ERC721Metadata extension, it should expose a 'tokenURI(uint256 tokenId)' method that
        /// can be interrogated to retrieve the token-specific URI.
        /// </summary>
        /// <returns>The URI for the given tokenId.</returns>
        public static async Task<string> GetTokenUriAsync(Web3 web3, string contractAddress, BigInteger tokenId)
        {
            var tokenUriFunctionMessage = new TokenUriFunction()
            {
                TokenId = tokenId
            };

            IContractQueryHandler<TokenUriFunction> balanceHandler = web3.Eth.GetContractQueryHandler<TokenUriFunction>();
            string uri = await balanceHandler.QueryAsync<string>(contractAddress, tokenUriFunctionMessage).ConfigureAwait(false);

            return uri;
        }

        public static async Task<string> SafeTransferAsync(Web3 web3, string contractAddress, string recipient, BigInteger tokenId)
        {
            IContractTransactionHandler<NftTransferFunction> transferHandler = web3.Eth.GetContractTransactionHandler<NftTransferFunction>();

            var transfer = new NftTransferFunction()
            {
                From = "",
                To = recipient,
                TokenId = tokenId
            };

            TransactionReceipt transactionTransferReceipt = await transferHandler.SendRequestAndWaitForReceiptAsync(contractAddress, transfer);

            return transactionTransferReceipt.TransactionHash;
        }

        public static async Task<string> MintAsync(Web3 web3, string contractAddress, string recipient, BigInteger tokenId, string tokenUri)
        {
            IContractTransactionHandler<NftMintFunction> mintHandler = web3.Eth.GetContractTransactionHandler<NftMintFunction>();

            var mint = new NftMintFunction()
            {
                Recipient = recipient,
                TokenId = tokenId,
				TokenUri = tokenUri
            };

            TransactionReceipt transactionMintReceipt = await mintHandler.SendRequestAndWaitForReceiptAsync(contractAddress, mint);

            return transactionMintReceipt.TransactionHash;
        }
	}

	/* Extensions to the ERC721 standard used by InterFlux:
	{
		"inputs": [
			{
				"internalType": "address",
				"name": "recipient",
				"type": "address"
			},
			{
				"internalType": "uint256",
				"name": "tokenId",
				"type": "uint256"
			},
			{
				"internalType": "string",
				"name": "tokenURI",
				"type": "string"
			}
		],
		"name": "mint",
		"outputs": [],
		"stateMutability": "nonpayable",
		"type": "function"
	},
	{
		"inputs": [
			{
				"internalType": "uint256",
				"name": "tokenId",
				"type": "uint256"
			}
		],
		"name": "burn",
		"outputs": [],
		"stateMutability": "nonpayable",
		"type": "function"
	},

	"42966c68": "burn(uint256)",
	"d3fc9864": "mint(address,uint256,string)",
    */

	// 60806040523480156200001157600080fd5b50604080518082018252600b81526a135a5b9d18589b1953919560aa1b60208083019182528351808501909452600384526213919560ea1b90840152815191929162000060916000916200007f565b508051620000769060019060208401906200007f565b50505062000161565b8280546200008d9062000125565b90600052602060002090601f016020900481019282620000b15760008555620000fc565b82601f10620000cc57805160ff1916838001178555620000fc565b82800160010185558215620000fc579182015b82811115620000fc578251825591602001919060010190620000df565b506200010a9291506200010e565b5090565b5b808211156200010a57600081556001016200010f565b600181811c908216806200013a57607f821691505b6020821081036200015b57634e487b7160e01b600052602260045260246000fd5b50919050565b6116e680620001716000396000f3fe608060405234801561001057600080fd5b50600436106100ea5760003560e01c806370a082311161008c578063b88d4fde11610066578063b88d4fde146101e1578063c87b56dd146101f4578063d0def52114610207578063e985e9c51461021a57600080fd5b806370a08231146101a557806395d89b41146101c6578063a22cb465146101ce57600080fd5b8063095ea7b3116100c8578063095ea7b31461015757806323b872dd1461016c57806342842e0e1461017f5780636352211e1461019257600080fd5b806301ffc9a7146100ef57806306fdde0314610117578063081812fc1461012c575b600080fd5b6101026100fd366004611181565b610256565b60405190151581526020015b60405180910390f35b61011f6102a8565b60405161010e91906111f6565b61013f61013a366004611209565b61033a565b6040516001600160a01b03909116815260200161010e565b61016a61016536600461123e565b6103c7565b005b61016a61017a366004611268565b6104dc565b61016a61018d366004611268565b61050d565b61013f6101a0366004611209565b610528565b6101b86101b33660046112a4565b61059f565b60405190815260200161010e565b61011f610626565b61016a6101dc3660046112bf565b610635565b61016a6101ef366004611387565b610644565b61011f610202366004611209565b61067c565b6101b8610215366004611403565b6107f2565b610102610228366004611465565b6001600160a01b03918216600090815260056020908152604080832093909416825291909152205460ff1690565b60006001600160e01b031982166380ac58cd60e01b148061028757506001600160e01b03198216635b5e139f60e01b145b806102a257506301ffc9a760e01b6001600160e01b03198316145b92915050565b6060600080546102b790611498565b80601f01602080910402602001604051908101604052809291908181526020018280546102e390611498565b80156103305780601f1061030557610100808354040283529160200191610330565b820191906000526020600020905b81548152906001019060200180831161031357829003601f168201915b5050505050905090565b60006103458261082a565b6103ab5760405162461bcd60e51b815260206004820152602c60248201527f4552433732313a20617070726f76656420717565727920666f72206e6f6e657860448201526b34b9ba32b73a103a37b5b2b760a11b60648201526084015b60405180910390fd5b506000908152600460205260409020546001600160a01b031690565b60006103d282610528565b9050806001600160a01b0316836001600160a01b03160361043f5760405162461bcd60e51b815260206004820152602160248201527f4552433732313a20617070726f76616c20746f2063757272656e74206f776e656044820152603960f91b60648201526084016103a2565b336001600160a01b038216148061045b575061045b8133610228565b6104cd5760405162461bcd60e51b815260206004820152603860248201527f4552433732313a20617070726f76652063616c6c6572206973206e6f74206f7760448201527f6e6572206e6f7220617070726f76656420666f7220616c6c000000000000000060648201526084016103a2565b6104d78383610847565b505050565b6104e633826108b5565b6105025760405162461bcd60e51b81526004016103a2906114d2565b6104d783838361099b565b6104d783838360405180602001604052806000815250610644565b6000818152600260205260408120546001600160a01b0316806102a25760405162461bcd60e51b815260206004820152602960248201527f4552433732313a206f776e657220717565727920666f72206e6f6e657869737460448201526832b73a103a37b5b2b760b91b60648201526084016103a2565b60006001600160a01b03821661060a5760405162461bcd60e51b815260206004820152602a60248201527f4552433732313a2062616c616e636520717565727920666f7220746865207a65604482015269726f206164647265737360b01b60648201526084016103a2565b506001600160a01b031660009081526003602052604090205490565b6060600180546102b790611498565b610640338383610b37565b5050565b61064e33836108b5565b61066a5760405162461bcd60e51b81526004016103a2906114d2565b61067684848484610c05565b50505050565b60606106878261082a565b6106ed5760405162461bcd60e51b815260206004820152603160248201527f45524337323155524953746f726167653a2055524920717565727920666f72206044820152703737b732bc34b9ba32b73a103a37b5b2b760791b60648201526084016103a2565b6000828152600660205260408120805461070690611498565b80601f016020809104026020016040519081016040528092919081815260200182805461073290611498565b801561077f5780601f106107545761010080835404028352916020019161077f565b820191906000526020600020905b81548152906001019060200180831161076257829003601f168201915b50505050509050600061079d60408051602081019091526000815290565b905080516000036107af575092915050565b8151156107e15780826040516020016107c9929190611523565b60405160208183030381529060405292505050919050565b6107ea84610c38565b949350505050565b6000610802600780546001019055565b600061080d60075490565b90506108198482610d0f565b6108238184610e42565b9392505050565b6000908152600260205260409020546001600160a01b0316151590565b600081815260046020526040902080546001600160a01b0319166001600160a01b038416908117909155819061087c82610528565b6001600160a01b03167f8c5be1e5ebec7d5bd14f71427d1e84f3dd0314c0f7b2291e5b200ac8c7c3b92560405160405180910390a45050565b60006108c08261082a565b6109215760405162461bcd60e51b815260206004820152602c60248201527f4552433732313a206f70657261746f7220717565727920666f72206e6f6e657860448201526b34b9ba32b73a103a37b5b2b760a11b60648201526084016103a2565b600061092c83610528565b9050806001600160a01b0316846001600160a01b031614806109675750836001600160a01b031661095c8461033a565b6001600160a01b0316145b806107ea57506001600160a01b0380821660009081526005602090815260408083209388168352929052205460ff166107ea565b826001600160a01b03166109ae82610528565b6001600160a01b031614610a125760405162461bcd60e51b815260206004820152602560248201527f4552433732313a207472616e736665722066726f6d20696e636f72726563742060448201526437bbb732b960d91b60648201526084016103a2565b6001600160a01b038216610a745760405162461bcd60e51b8152602060048201526024808201527f4552433732313a207472616e7366657220746f20746865207a65726f206164646044820152637265737360e01b60648201526084016103a2565b610a7f600082610847565b6001600160a01b0383166000908152600360205260408120805460019290610aa8908490611568565b90915550506001600160a01b0382166000908152600360205260408120805460019290610ad690849061157f565b909155505060008181526002602052604080822080546001600160a01b0319166001600160a01b0386811691821790925591518493918716917fddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef91a4505050565b816001600160a01b0316836001600160a01b031603610b985760405162461bcd60e51b815260206004820152601960248201527f4552433732313a20617070726f766520746f2063616c6c65720000000000000060448201526064016103a2565b6001600160a01b03838116600081815260056020908152604080832094871680845294825291829020805460ff191686151590811790915591519182527f17307eab39ab6107e8899845ad3d59bd9653f200f220920489ca2b5937696c31910160405180910390a3505050565b610c1084848461099b565b610c1c84848484610ecd565b6106765760405162461bcd60e51b81526004016103a290611597565b6060610c438261082a565b610ca75760405162461bcd60e51b815260206004820152602f60248201527f4552433732314d657461646174613a2055524920717565727920666f72206e6f60448201526e3732bc34b9ba32b73a103a37b5b2b760891b60648201526084016103a2565b6000610cbe60408051602081019091526000815290565b90506000815111610cde5760405180602001604052806000815250610823565b80610ce884610fce565b604051602001610cf9929190611523565b6040516020818303038152906040529392505050565b6001600160a01b038216610d655760405162461bcd60e51b815260206004820181905260248201527f4552433732313a206d696e7420746f20746865207a65726f206164647265737360448201526064016103a2565b610d6e8161082a565b15610dbb5760405162461bcd60e51b815260206004820152601c60248201527f4552433732313a20746f6b656e20616c7265616479206d696e7465640000000060448201526064016103a2565b6001600160a01b0382166000908152600360205260408120805460019290610de490849061157f565b909155505060008181526002602052604080822080546001600160a01b0319166001600160a01b03861690811790915590518392907fddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef908290a45050565b610e4b8261082a565b610eae5760405162461bcd60e51b815260206004820152602e60248201527f45524337323155524953746f726167653a2055524920736574206f66206e6f6e60448201526d32bc34b9ba32b73a103a37b5b2b760911b60648201526084016103a2565b600082815260066020908152604090912082516104d7928401906110cf565b60006001600160a01b0384163b15610fc357604051630a85bd0160e11b81526001600160a01b0385169063150b7a0290610f119033908990889088906004016115e9565b6020604051808303816000875af1925050508015610f4c575060408051601f3d908101601f19168201909252610f4991810190611626565b60015b610fa9573d808015610f7a576040519150601f19603f3d011682016040523d82523d6000602084013e610f7f565b606091505b508051600003610fa15760405162461bcd60e51b81526004016103a290611597565b805181602001fd5b6001600160e01b031916630a85bd0160e11b1490506107ea565b506001949350505050565b606081600003610ff55750506040805180820190915260018152600360fc1b602082015290565b8160005b811561101f578061100981611643565b91506110189050600a83611672565b9150610ff9565b60008167ffffffffffffffff81111561103a5761103a6112fb565b6040519080825280601f01601f191660200182016040528015611064576020820181803683370190505b5090505b84156107ea57611079600183611568565b9150611086600a86611686565b61109190603061157f565b60f81b8183815181106110a6576110a661169a565b60200101906001600160f81b031916908160001a9053506110c8600a86611672565b9450611068565b8280546110db90611498565b90600052602060002090601f0160209004810192826110fd5760008555611143565b82601f1061111657805160ff1916838001178555611143565b82800160010185558215611143579182015b82811115611143578251825591602001919060010190611128565b5061114f929150611153565b5090565b5b8082111561114f5760008155600101611154565b6001600160e01b03198116811461117e57600080fd5b50565b60006020828403121561119357600080fd5b813561082381611168565b60005b838110156111b95781810151838201526020016111a1565b838111156106765750506000910152565b600081518084526111e281602086016020860161119e565b601f01601f19169290920160200192915050565b60208152600061082360208301846111ca565b60006020828403121561121b57600080fd5b5035919050565b80356001600160a01b038116811461123957600080fd5b919050565b6000806040838503121561125157600080fd5b61125a83611222565b946020939093013593505050565b60008060006060848603121561127d57600080fd5b61128684611222565b925061129460208501611222565b9150604084013590509250925092565b6000602082840312156112b657600080fd5b61082382611222565b600080604083850312156112d257600080fd5b6112db83611222565b9150602083013580151581146112f057600080fd5b809150509250929050565b634e487b7160e01b600052604160045260246000fd5b600067ffffffffffffffff8084111561132c5761132c6112fb565b604051601f8501601f19908116603f01168101908282118183101715611354576113546112fb565b8160405280935085815286868601111561136d57600080fd5b858560208301376000602087830101525050509392505050565b6000806000806080858703121561139d57600080fd5b6113a685611222565b93506113b460208601611222565b925060408501359150606085013567ffffffffffffffff8111156113d757600080fd5b8501601f810187136113e857600080fd5b6113f787823560208401611311565b91505092959194509250565b6000806040838503121561141657600080fd5b61141f83611222565b9150602083013567ffffffffffffffff81111561143b57600080fd5b8301601f8101851361144c57600080fd5b61145b85823560208401611311565b9150509250929050565b6000806040838503121561147857600080fd5b61148183611222565b915061148f60208401611222565b90509250929050565b600181811c908216806114ac57607f821691505b6020821081036114cc57634e487b7160e01b600052602260045260246000fd5b50919050565b60208082526031908201527f4552433732313a207472616e736665722063616c6c6572206973206e6f74206f6040820152701ddb995c881b9bdc88185c1c1c9bdd9959607a1b606082015260800190565b6000835161153581846020880161119e565b83519083019061154981836020880161119e565b01949350505050565b634e487b7160e01b600052601160045260246000fd5b60008282101561157a5761157a611552565b500390565b6000821982111561159257611592611552565b500190565b60208082526032908201527f4552433732313a207472616e7366657220746f206e6f6e20455243373231526560408201527131b2b4bb32b91034b6b83632b6b2b73a32b960711b606082015260800190565b6001600160a01b038581168252841660208201526040810183905260806060820181905260009061161c908301846111ca565b9695505050505050565b60006020828403121561163857600080fd5b815161082381611168565b60006001820161165557611655611552565b5060010190565b634e487b7160e01b600052601260045260246000fd5b6000826116815761168161165c565b500490565b6000826116955761169561165c565b500690565b634e487b7160e01b600052603260045260246000fdfea264697066735822122042368c8df2a26d5c5210fe3b8ac9c93c5443667e38f4937353a267d93e0dfea164736f6c634300080d0033

	/*
        {
            "095ea7b3": "approve(address,uint256)",
            "70a08231": "balanceOf(address)",
            "081812fc": "getApproved(uint256)",
            "e985e9c5": "isApprovedForAll(address,address)",
            "d0def521": "mint(address,string)",
            "06fdde03": "name()",
            "6352211e": "ownerOf(uint256)",
            "42842e0e": "safeTransferFrom(address,address,uint256)",
            "b88d4fde": "safeTransferFrom(address,address,uint256,bytes)",
            "a22cb465": "setApprovalForAll(address,bool)",
            "01ffc9a7": "supportsInterface(bytes4)",
            "95d89b41": "symbol()",
            "c87b56dd": "tokenURI(uint256)",
            "23b872dd": "transferFrom(address,address,uint256)"
        }
     */

	/*
        [
	        {
		        "inputs": [],
		        "stateMutability": "nonpayable",
		        "type": "constructor"
	        },
	        {
		        "anonymous": false,
		        "inputs": [
			        {
				        "indexed": true,
				        "internalType": "address",
				        "name": "owner",
				        "type": "address"
			        },
			        {
				        "indexed": true,
				        "internalType": "address",
				        "name": "approved",
				        "type": "address"
			        },
			        {
				        "indexed": true,
				        "internalType": "uint256",
				        "name": "tokenId",
				        "type": "uint256"
			        }
		        ],
		        "name": "Approval",
		        "type": "event"
	        },
	        {
		        "anonymous": false,
		        "inputs": [
			        {
				        "indexed": true,
				        "internalType": "address",
				        "name": "owner",
				        "type": "address"
			        },
			        {
				        "indexed": true,
				        "internalType": "address",
				        "name": "operator",
				        "type": "address"
			        },
			        {
				        "indexed": false,
				        "internalType": "bool",
				        "name": "approved",
				        "type": "bool"
			        }
		        ],
		        "name": "ApprovalForAll",
		        "type": "event"
	        },
	        {
		        "anonymous": false,
		        "inputs": [
			        {
				        "indexed": true,
				        "internalType": "address",
				        "name": "from",
				        "type": "address"
			        },
			        {
				        "indexed": true,
				        "internalType": "address",
				        "name": "to",
				        "type": "address"
			        },
			        {
				        "indexed": true,
				        "internalType": "uint256",
				        "name": "tokenId",
				        "type": "uint256"
			        }
		        ],
		        "name": "Transfer",
		        "type": "event"
	        },
	        {
		        "inputs": [
			        {
				        "internalType": "address",
				        "name": "to",
				        "type": "address"
			        },
			        {
				        "internalType": "uint256",
				        "name": "tokenId",
				        "type": "uint256"
			        }
		        ],
		        "name": "approve",
		        "outputs": [],
		        "stateMutability": "nonpayable",
		        "type": "function"
	        },
	        {
		        "inputs": [
			        {
				        "internalType": "address",
				        "name": "owner",
				        "type": "address"
			        }
		        ],
		        "name": "balanceOf",
		        "outputs": [
			        {
				        "internalType": "uint256",
				        "name": "",
				        "type": "uint256"
			        }
		        ],
		        "stateMutability": "view",
		        "type": "function"
	        },
	        {
		        "inputs": [
			        {
				        "internalType": "uint256",
				        "name": "tokenId",
				        "type": "uint256"
			        }
		        ],
		        "name": "getApproved",
		        "outputs": [
			        {
				        "internalType": "address",
				        "name": "",
				        "type": "address"
			        }
		        ],
		        "stateMutability": "view",
		        "type": "function"
	        },
	        {
		        "inputs": [
			        {
				        "internalType": "address",
				        "name": "owner",
				        "type": "address"
			        },
			        {
				        "internalType": "address",
				        "name": "operator",
				        "type": "address"
			        }
		        ],
		        "name": "isApprovedForAll",
		        "outputs": [
			        {
				        "internalType": "bool",
				        "name": "",
				        "type": "bool"
			        }
		        ],
		        "stateMutability": "view",
		        "type": "function"
	        },
	        {
		        "inputs": [
			        {
				        "internalType": "address",
				        "name": "recipient",
				        "type": "address"
			        },
			        {
				        "internalType": "string",
				        "name": "tokenURI",
				        "type": "string"
			        }
		        ],
		        "name": "mint",
		        "outputs": [
			        {
				        "internalType": "uint256",
				        "name": "",
				        "type": "uint256"
			        }
		        ],
		        "stateMutability": "nonpayable",
		        "type": "function"
	        },
	        {
		        "inputs": [],
		        "name": "name",
		        "outputs": [
			        {
				        "internalType": "string",
				        "name": "",
				        "type": "string"
			        }
		        ],
		        "stateMutability": "view",
		        "type": "function"
	        },
	        {
		        "inputs": [
			        {
				        "internalType": "uint256",
				        "name": "tokenId",
				        "type": "uint256"
			        }
		        ],
		        "name": "ownerOf",
		        "outputs": [
			        {
				        "internalType": "address",
				        "name": "",
				        "type": "address"
			        }
		        ],
		        "stateMutability": "view",
		        "type": "function"
	        },
	        {
		        "inputs": [
			        {
				        "internalType": "address",
				        "name": "from",
				        "type": "address"
			        },
			        {
				        "internalType": "address",
				        "name": "to",
				        "type": "address"
			        },
			        {
				        "internalType": "uint256",
				        "name": "tokenId",
				        "type": "uint256"
			        }
		        ],
		        "name": "safeTransferFrom",
		        "outputs": [],
		        "stateMutability": "nonpayable",
		        "type": "function"
	        },
	        {
		        "inputs": [
			        {
				        "internalType": "address",
				        "name": "from",
				        "type": "address"
			        },
			        {
				        "internalType": "address",
				        "name": "to",
				        "type": "address"
			        },
			        {
				        "internalType": "uint256",
				        "name": "tokenId",
				        "type": "uint256"
			        },
			        {
				        "internalType": "bytes",
				        "name": "_data",
				        "type": "bytes"
			        }
		        ],
		        "name": "safeTransferFrom",
		        "outputs": [],
		        "stateMutability": "nonpayable",
		        "type": "function"
	        },
	        {
		        "inputs": [
			        {
				        "internalType": "address",
				        "name": "operator",
				        "type": "address"
			        },
			        {
				        "internalType": "bool",
				        "name": "approved",
				        "type": "bool"
			        }
		        ],
		        "name": "setApprovalForAll",
		        "outputs": [],
		        "stateMutability": "nonpayable",
		        "type": "function"
	        },
	        {
		        "inputs": [
			        {
				        "internalType": "bytes4",
				        "name": "interfaceId",
				        "type": "bytes4"
			        }
		        ],
		        "name": "supportsInterface",
		        "outputs": [
			        {
				        "internalType": "bool",
				        "name": "",
				        "type": "bool"
			        }
		        ],
		        "stateMutability": "view",
		        "type": "function"
	        },
	        {
		        "inputs": [],
		        "name": "symbol",
		        "outputs": [
			        {
				        "internalType": "string",
				        "name": "",
				        "type": "string"
			        }
		        ],
		        "stateMutability": "view",
		        "type": "function"
	        },
	        {
		        "inputs": [
			        {
				        "internalType": "uint256",
				        "name": "tokenId",
				        "type": "uint256"
			        }
		        ],
		        "name": "tokenURI",
		        "outputs": [
			        {
				        "internalType": "string",
				        "name": "",
				        "type": "string"
			        }
		        ],
		        "stateMutability": "view",
		        "type": "function"
	        },
	        {
		        "inputs": [
			        {
				        "internalType": "address",
				        "name": "from",
				        "type": "address"
			        },
			        {
				        "internalType": "address",
				        "name": "to",
				        "type": "address"
			        },
			        {
				        "internalType": "uint256",
				        "name": "tokenId",
				        "type": "uint256"
			        }
		        ],
		        "name": "transferFrom",
		        "outputs": [],
		        "stateMutability": "nonpayable",
		        "type": "function"
	        }
        ]
     */
}
