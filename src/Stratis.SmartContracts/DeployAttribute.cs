using System;

namespace Stratis.SmartContracts
{
    /// <summary>
    /// When there are multiple contracts inside an assembly, specifies that this Type is the one to deploy as part of a contract
    /// creation transaction. Must only be used on a single Type within an assembly, or contract validation will fail.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class DeployAttribute : Attribute
    {   
    }
}
