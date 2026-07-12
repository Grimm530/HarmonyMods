using System;
using Rust;

namespace AlphaLoot.Harmony;

public class ItemAmountWeighted : ItemAmountRanged
{
	public int Weight = 1;

	public Era[] RestrictedEras = Array.Empty<Era>();
}
