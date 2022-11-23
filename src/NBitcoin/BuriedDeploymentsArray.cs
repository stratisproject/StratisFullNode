using System;

namespace NBitcoin
{
    public class BuriedDeploymentsArray
    {
        protected int[] heights;

        public BuriedDeploymentsArray()
        {
            this.heights = new int[0];
        }

        protected int EnsureIndex(int index)
        {
            if (index >= this.heights.Length)
                Array.Resize(ref this.heights, index + 1);
            
            return index;
        }

        public int this[BuriedDeployments index]
        {
            get => this.heights[EnsureIndex((int)index)];
            set => this.heights[EnsureIndex((int)index)] = value;
        }
    }

    public enum BuriedDeployments
    {
        /// <summary>
        /// Height in coinbase.
        /// </summary>
        BIP34,

        /// <summary>
        /// Height in OP_CLTV.
        /// </summary>
        BIP65,

        /// <summary>
        /// Strict DER signature.
        /// </summary>
        BIP66
    }
}