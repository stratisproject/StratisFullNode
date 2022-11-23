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

        protected void EnsureIndex(int index)
        {
            if (index >= this.heights.Length)
                Array.Resize(ref this.heights, index + 1);
        }

        public int this[BuriedDeployments index]
        {
            get { EnsureIndex((int)index); return this.heights[(int)index]; }
            set { EnsureIndex((int)index); this.heights[(int)index] = value; }
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