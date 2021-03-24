using NBitcoin;

namespace Stratis.Bitcoin.Features.Interop
{
    public enum InteropRequestType
    {
        /// <summary>Stratis (Cirrus) network invoking Ethereum contract.</summary>
        InvokeEthereum = 0,

        /// <summary>Ethereum network invoking Stratis (Cirrus) contract.</summary>
        InvokeStratis = 1
    }

    /// <summary>
    /// This class is dual-purposed to store both types of interop requests (either direction), so
    /// the meaning of some of the properties varies slightly depending on which direction the request
    /// is being made in.
    /// </summary>
    public class InteropRequest : IBitcoinSerializable
    {
        private string requestId;

        private string transactionId;

        private int requestType;

        private string targetContractAddress;

        private string sourceAddress;

        private string methodName;

        private string[] parameters;

        private bool processed;

        private string response;

        /// <summary>
        /// The unique identifier for this particular remote contract invocation request.
        /// It gets selected by the request creator.
        /// </summary>
        public string RequestId { get { return this.requestId; } set { this.requestId = value; } }

        /// <summary>
        /// For an interop request for a Stratis contract, this is the txid of the contract call transaction.
        /// For an Ethereum invocation this gets populated with the txid on the Ethereum chain of the transaction that was used to store the interop result.
        /// </summary>
        public string TransactionId { get { return this.transactionId; } set { this.transactionId = value; } }

        public int RequestType { get { return this.requestType; } set { this.requestType = value; } }

        /// <summary>
        /// For an Ethereum invocation, the contract address stores the interop contract on the Stratis network that initiated the request.
        /// This is to facilitate the response being stored against the same contract when it is available.
        /// For a Stratis invocation, this is the address of the contract to be invoked.
        /// </summary>
        public string TargetContractAddress { get { return this.targetContractAddress; } set { this.targetContractAddress = value; } }

        /// <summary>
        /// For an Ethereum invocation, this stores the origin of the transaction on the Cirrus network (ultimately this should support both a contract address or a normal address).
        /// For a Stratis invocation, this is the address of the contract (or normal account) requesting the invocation on the Ethereum side, if available.
        /// </summary>
        public string SourceAddress { get { return this.sourceAddress; } set { this.sourceAddress = value; } }

        /// <summary>
        /// For either type of invocation, this is the name of the method that is to be invoked.
        /// </summary>
        public string MethodName { get { return this.methodName; } set { this.methodName = value; } }

        /// <summary>
        /// For either type of invocation, these are the parameters to be passed to the specified method.
        /// </summary>
        public string[] Parameters { get { return this.parameters; } set { this.parameters = value; } }

        /// <summary>
        /// Indicates whether or not this request has been processed by the poller.
        /// </summary>
        public bool Processed { get { return this.processed; } set { this.processed = value; } }

        /// <summary>
        /// The return value of the method requested for invocation.
        /// This is defaulted to an empty string for serialization purposes, but this field only has meaning when Processed is true.
        /// </summary>
        public string Response { get { return this.response; } set { this.response = value; } }

        public void ReadWrite(BitcoinStream s)
        {
            s.ReadWrite(ref this.requestId);
            s.ReadWrite(ref this.transactionId);
            s.ReadWrite(ref this.requestType);
            s.ReadWrite(ref this.targetContractAddress);
            s.ReadWrite(ref this.sourceAddress);
            s.ReadWrite(ref this.methodName);
            s.ReadWrite(ref this.parameters);
            s.ReadWrite(ref this.processed);
            s.ReadWrite(ref this.response);
        }
    }
}
