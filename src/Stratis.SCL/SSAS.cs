using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using NBitcoin;
using NBitcoin.DataEncoders;
using Nethereum.RLP;
using Stratis.SmartContracts;

namespace Stratis.SCL.Crypto
{
    public static class SSAS
    {
        public static byte[] ValidateAndParse(Address address, string url, byte[] signature, string signatureTemplateMap)
        {
            // Validate the signature.
            var key = PubKey.RecoverFromMessage(url, Encoders.Base64.EncodeData(signature));
            if (key.Hash != new KeyId(address.ToBytes()))
                return null;

            try
            {
                // Create a mapping of available url arguments.
                Dictionary<string, StringValues> argDict = QueryHelpers.ParseQuery(new Uri(url).Query);

                // The "signatureTemplateMap" takes the following form: "uid#11,symbol#4,amount#12,targetAddres#4,targetNetwork#4"
                var arguments = signatureTemplateMap
                    .Split(",", StringSplitOptions.RemoveEmptyEntries)
                    .Select(argName =>
                    {
                        var argNameSplit = argName.Split("#");
                        var argValue = argDict[argNameSplit[0]].ToString();
                        var fieldType = int.Parse(argNameSplit[1]);
                        var hexEncoder = new HexEncoder();
                        byte[] argumentBytes;
                        switch (fieldType)
                        {
                            case 4:
                                argumentBytes = Encoders.ASCII.DecodeData(argValue);
                                break;
                            case 11:
                                // URL's should contain UInt128 as 16 bytes encoded in hexadecimal.
                                if (argValue.Length != 32)
                                    return null;
                                argumentBytes = hexEncoder.DecodeData(argValue);
                                break;
                            case 12:
                                // If the value contains a "." then its being passed as an amount.
                                // We assume 8 decimals for the conversion to UInt256.
                                if (argValue.Contains('.'))
                                {
                                    decimal amt = decimal.Parse(argValue);
                                    // Convert to UInt256.
                                    argumentBytes = ((UInt256)((ulong)(amt * 100000000 /* 8 decimals */))).ToBytes();
                                }
                                else
                                {
                                    // URL's should contain UInt256 as 32 bytes encoded in hexadecimal.
                                    if (argValue.Length != 32)
                                        return null;
                                    argumentBytes = hexEncoder.DecodeData(argValue);
                                }
                                break;
                            // TODO: Handle more types as required.
                            default:
                                return null;
                        }
                        return argumentBytes;
                    })
                    .ToArray();
                // Convert the list of objects to RLE.
                return RLP.EncodeElementsAndList(arguments);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
