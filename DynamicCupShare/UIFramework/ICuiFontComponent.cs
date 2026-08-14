using UnityEngine;

namespace Oxide.Ext.Chaos.UIFramework;

public interface ICuiFontComponent
{
	string Text { get; set; }

	Font Font { get; set; }

	int FontSize { get; set; }

	TextAnchor Alignment { get; set; }
}
