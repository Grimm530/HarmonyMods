using System;

namespace CustomGenerator.Utility;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class SchemaAttribute : Attribute
{
	public double Min = double.NaN;

	public double Max = double.NaN;

	public Type Enum;

	public string[] Values;

	public bool ReadOnly;

	public string ItemTitle;

	public bool Addable;

	public double SumTo = double.NaN;

	public string SumFields;

	public string Suggest;
}
