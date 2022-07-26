﻿using NBitcoin;

namespace Stratis.Bitcoin.Features.SmartContracts.MetadataTracker
{
    public enum MetadataTableNumber
    {
        // Don't exceed 63.
        GBPT = 0
    }

    public class MetadataTrackerDefinition
    {
        public MetadataTableNumber TableNumber { get; set; }

        public string Contract { get; set; }

        public string LogType { get; set; }

        public int MetadataTopic { get; set; }

        public int FirstBlock { get; set; }

        public BlockLocator BlockLocator;
    }
}
