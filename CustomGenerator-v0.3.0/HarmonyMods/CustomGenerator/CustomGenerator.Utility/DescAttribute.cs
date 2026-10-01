using System;

namespace CustomGenerator.Utility;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class DescAttribute : Attribute
{
	public readonly string En;

	public readonly string Ru;

	public DescAttribute(string en, string ru)
	{
		En = en;
		Ru = ru;
	}
}
