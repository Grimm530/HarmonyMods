using System;

namespace Thorium.Rust.Models;

[AttributeUsage(AttributeTargets.Method)]
public class RPC_Extras : Attribute
{
    public readonly uint[] rpcs;

    public RPC_Extras(uint[] rpcs)
    {
        this.rpcs = rpcs;
    }
}