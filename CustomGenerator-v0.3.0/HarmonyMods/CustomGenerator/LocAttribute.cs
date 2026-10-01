using System;

namespace CustomGenerator;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class LocAttribute : Attribute
{
	public readonly string En;

	public readonly string Ru;

	public LocAttribute(string en, string ru)
	{
		En = en;
		Ru = ru;
	}
}
