using UnityEngine;

namespace Oxide.Ext.Chaos.UIFramework;

public class HorizontalLayoutGroup : BaseLayoutGroup
{
	public HorizontalLayoutGroup(int columns)
		: base(columns, 1, Axis.Horizontal)
	{
	}

	public HorizontalLayoutGroup()
		: base(1, 1, Axis.Horizontal)
	{
	}

	public override void RecalculateSize()
	{
		base.RecalculateSize();
		m_Rows = 1;
	}

	public override void ResizeContentToFit(RectTransformComponent transform, int numberOfItems)
	{
		m_Size = m_FixedSize;
		float num4 = default(float);
		float num3 = default(float);
		while (true)
		{
			int num = -1574198104;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ -60244148)) % 9)
				{
				case 0u:
					break;
				default:
					return;
				case 2u:
					return;
				case 1u:
				{
					int num7;
					int num8;
					if (m_Size.y > 0f)
					{
						num7 = -1298781841;
						num8 = num7;
					}
					else
					{
						num7 = -1441995230;
						num8 = num7;
					}
					num = num7 ^ ((int)num2 * -527425217);
					continue;
				}
				case 5u:
					num4 = Mathf.Max(m_ViewportSize.x, m_Padding.Horizontal + (float)numberOfItems * m_Size.x + (float)Mathf.Max(numberOfItems - 1, 0) * m_Spacing.Horizontal);
					num3 = Mathf.Max(m_ViewportSize.y, m_Padding.Vertical + m_Size.y);
					num = -600619698;
					continue;
				case 8u:
				{
					int num5;
					int num6;
					if (!(m_Size.x <= 0f))
					{
						num5 = -966352133;
						num6 = num5;
					}
					else
					{
						num5 = -70140373;
						num6 = num5;
					}
					num = num5 ^ (int)(num2 * 398169695);
					continue;
				}
				case 3u:
					m_Area = new Area(0f - num4 * 0.5f, 0f - m_ViewportSize.y * 0.5f, num4 * 0.5f, m_ViewportSize.y * 0.5f);
					num = ((int)num2 * -224599257) ^ -805289847;
					continue;
				case 4u:
					m_Offset = new Vector2(m_Area.Width - (float)m_Columns * m_Size.x - (float)(m_Columns - 1) * m_Spacing.Horizontal - (m_Padding.Left + m_Padding.Right), 0f - (m_Area.Height - (float)m_Rows * m_Size.y - (float)(m_Rows - 1) * m_Spacing.Vertical - (m_Padding.Bottom + m_Padding.Top)));
					num = (int)((num2 * 1862422999) ^ 0x62CBD394);
					continue;
				case 6u:
					transform.Set(new Offset(0f, 0f - (num3 - m_ViewportSize.y), num4 - m_ViewportSize.x, 0f));
					m_Columns = numberOfItems;
					num = ((int)num2 * -1944390459) ^ -1267878052;
					continue;
				case 7u:
					return;
				}
				break;
			}
		}
	}
}
