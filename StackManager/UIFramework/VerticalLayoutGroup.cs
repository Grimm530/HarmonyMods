using UnityEngine;

namespace Oxide.Ext.Chaos.UIFramework;

public class VerticalLayoutGroup : BaseLayoutGroup
{
	public VerticalLayoutGroup(int rows)
		: base(1, rows, Axis.Vertical)
	{
	}

	public VerticalLayoutGroup()
		: base(1, 1, Axis.Vertical)
	{
	}

	public override void RecalculateSize()
	{
		base.RecalculateSize();
		while (true)
		{
			int num = 551276965;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ 0x6ABD483B)) % 3)
				{
				case 2u:
					break;
				default:
					return;
				case 1u:
					goto IL_0028;
				case 0u:
					return;
				}
				break;
				IL_0028:
				m_Columns = 1;
				num = (int)(num2 * 932985643) ^ -2055006915;
			}
		}
	}

	public override void ResizeContentToFit(RectTransformComponent transform, int numberOfItems)
	{
		m_Size = m_FixedSize;
		if (m_Size.x <= 0f)
		{
			return;
		}
		float num6 = default(float);
		while (true)
		{
			int num = -216618109;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ -192241887)) % 6)
				{
				case 4u:
					break;
				case 5u:
					num6 = Mathf.Max(m_ViewportSize.y, m_Padding.Vertical + (float)numberOfItems * m_Size.y + (float)Mathf.Max(numberOfItems - 1, 0) * m_Spacing.Vertical);
					num = -339309656;
					continue;
				case 1u:
					return;
				case 3u:
				{
					float num5 = Mathf.Max(m_ViewportSize.x, m_Padding.Horizontal + m_Size.x);
					transform.Set(new Offset(0f, 0f - (num6 - m_ViewportSize.y), num5 - m_ViewportSize.x, 0f));
					m_Rows = numberOfItems;
					m_Area = new Area(0f - m_ViewportSize.x * 0.5f, 0f - num6 * 0.5f, m_ViewportSize.x * 0.5f, num6 * 0.5f);
					num = ((int)num2 * -1576447579) ^ 0x62BF35C4;
					continue;
				}
				case 2u:
				{
					int num3;
					int num4;
					if (m_Size.y > 0f)
					{
						num3 = -424348490;
						num4 = num3;
					}
					else
					{
						num3 = -20560018;
						num4 = num3;
					}
					num = num3 ^ (int)(num2 * 1955038597);
					continue;
				}
				default:
					m_Offset = new Vector2(m_Area.Width - (float)m_Columns * m_Size.x - (float)(m_Columns - 1) * m_Spacing.Horizontal - (m_Padding.Left + m_Padding.Right), 0f - (m_Area.Height - (float)m_Rows * m_Size.y - (float)(m_Rows - 1) * m_Spacing.Vertical - (m_Padding.Bottom + m_Padding.Top)));
					return;
				}
				break;
			}
		}
	}
}
