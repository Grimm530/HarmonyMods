using Unity.Mathematics;
using UnityEngine;

namespace Oxide.Ext.Chaos.UIFramework;

public abstract class BaseLayoutGroup
{
	protected int m_Columns;

	protected int m_Rows;

	protected bool m_IsScrollable;

	protected float2 m_Size;

	protected float2 m_ViewportSize;

	protected float2 m_FixedSize;

	protected Vector2Int m_FixedCount;

	protected float2 m_Offset;

	protected Area m_Area = Area.full;

	protected Spacing m_Spacing = Spacing.zero;

	protected Padding m_Padding = Padding.zero;

	protected Axis m_Axis;

	protected Corner m_Corner;

	public Area Area
	{
		get
		{
			return m_Area;
		}
		set
		{
			m_Area = value;
		}
	}

	public Axis Axis
	{
		get
		{
			return m_Axis;
		}
		set
		{
			m_Axis = value;
		}
	}

	public Spacing Spacing
	{
		get
		{
			return m_Spacing;
		}
		set
		{
			m_Spacing = value;
		}
	}

	public Padding Padding
	{
		get
		{
			return m_Padding;
		}
		set
		{
			m_Padding = value;
		}
	}

	public Corner Corner
	{
		get
		{
			return m_Corner;
		}
		set
		{
			m_Corner = value;
		}
	}

	public Vector2 FixedSize
	{
		get
		{
			return m_FixedSize;
		}
		set
		{
			m_FixedSize = value;
		}
	}

	public Vector2Int FixedCount
	{
		get
		{
			return m_FixedCount;
		}
		set
		{
			m_FixedCount = value;
		}
	}

	public bool IsScrollable
	{
		get
		{
			return m_IsScrollable;
		}
		set
		{
			m_IsScrollable = value;
		}
	}

	public float2 ViewportSize
	{
		get
		{
			return m_ViewportSize;
		}
		set
		{
			m_ViewportSize = value;
		}
	}

	protected bool HasFixedSize
	{
		get
		{
			if (!(m_FixedSize.x > 0f))
				return m_FixedSize.y > 0f;
			return true;
		}
	}

	public int PerPage => m_Columns * m_Rows;

	public bool HasPreviousPage(int page)
	{
		return page > 0;
	}

	public bool HasNextPage(int page, int count)
	{
		return count > PerPage * (page + 1);
	}

	protected BaseLayoutGroup(int columns, int rows, Axis axis)
	{
		while (true)
		{
			int num = 433424223;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ 0x2FD2D220)) % 4)
				{
				case 2u:
					break;
				case 3u:
					m_Columns = columns;
					num = ((int)num2 * -952359909) ^ 0x35B9B855;
					continue;
				case 0u:
					m_Rows = rows;
					num = (int)(num2 * 1071664766) ^ -1793795515;
					continue;
				default:
					m_Axis = axis;
					return;
				}
				break;
			}
		}
	}

	public virtual void RecalculateSize()
	{
		if (HasFixedSize)
		{
			goto IL_000b;
		}
		goto IL_0131;
		IL_000b:
		int num = 1809665122;
		goto IL_0010;
		IL_0010:
		float f2 = default(float);
		while (true)
		{
			uint num2;
			switch ((num2 = (uint)(num ^ 0x2730283F)) % 11)
			{
			case 5u:
				break;
			case 0u:
				num = (int)(num2 * 778226400) ^ -1811757428;
				continue;
			case 8u:
				m_Columns = FixedCount.x;
				m_Rows = FixedCount.y;
				num = (int)(num2 * 1831669191) ^ -478780680;
				continue;
			case 3u:
				m_Columns = MaxInSpace(m_Area.Width - (m_Padding.Left + m_Padding.Right), m_Size.x, m_Spacing.Horizontal);
				num = 762010694;
				continue;
			case 10u:
				f2 = (m_Area.Width - m_Padding.Horizontal - m_Spacing.Horizontal * (float)(m_Columns - 1)) / (float)m_Columns;
				num = ((int)num2 * -934058344) ^ -1210301840;
				continue;
			case 1u:
				goto IL_0131;
			case 7u:
				m_Rows = MaxInSpace(m_Area.Height - (m_Padding.Top + m_Padding.Bottom), m_Size.y, m_Spacing.Vertical);
				num = (int)(num2 * 1535812692) ^ -121106664;
				continue;
			case 2u:
			{
				m_Size = m_FixedSize;
				int num3;
				int num4;
				if (FixedCount != Vector2Int.zero)
				{
					num3 = 766476225;
					num4 = num3;
				}
				else
				{
					num3 = 1835801669;
					num4 = num3;
				}
				num = num3 ^ (int)(num2 * 1064443494);
				continue;
			}
			case 9u:
				return;
			case 4u:
				m_Offset = new Vector2(m_Area.Width - (float)m_Columns * m_Size.x - (float)(m_Columns - 1) * m_Spacing.Horizontal - (m_Padding.Left + m_Padding.Right), 0f - (m_Area.Height - (float)m_Rows * m_Size.y - (float)(m_Rows - 1) * m_Spacing.Vertical - (m_Padding.Bottom + m_Padding.Top)));
				num = 1665576108;
				continue;
			default:
			{
				float f = (m_Area.Height - m_Padding.Vertical - m_Spacing.Vertical * (float)(m_Rows - 1)) / (float)m_Rows;
				m_Size = new float2(Mathf.Abs(f2), Mathf.Abs(f));
				return;
			}
			}
			break;
		}
		goto IL_000b;
		IL_0131:
		m_Offset = Vector2.zero;
		num = 497186081;
		goto IL_0010;
	}

	public virtual void ResizeContentToFit(RectTransformComponent transform, int numberOfItems)
	{
	}

	protected int MaxInSpace(float usableArea, float size, float spacing)
	{
		float num = size + spacing;
		int num2 = Mathf.FloorToInt(usableArea / num);
		while (true)
		{
			int num3 = -1062243202;
			while (true)
			{
				uint num4;
				int num5;
				switch ((num4 = (uint)(num3 ^ -4289117)) % 4)
				{
				case 3u:
					break;
				case 1u:
				{
					int num6;
					if (!((float)num2 * size + (float)(num2 - 1) * spacing + size + spacing > usableArea))
					{
						num5 = -306749029;
						num6 = num5;
					}
					else
					{
						num5 = -353196663;
						num6 = num5;
					}
					goto IL_0053;
				}
				case 0u:
					return num2;
				default:
					return num2 + 1;
				}
				break;
				IL_0053:
				num3 = num5 ^ (int)(num4 * 1713066662);
			}
		}
	}

	public void Evaluate(int index, out Anchor anchor, out Offset offset)
	{
		anchor = Anchor.Center;
		int columnNumber = default(int);
		float num3 = default(float);
		int rowNumber = default(int);
		float num4 = default(float);
		while (true)
		{
			int num = 1543737288;
			while (true)
			{
				uint num2;
				switch ((num2 = (uint)(num ^ 0x52A1D561)) % 19)
				{
				case 10u:
					break;
				case 18u:
					columnNumber = m_Columns - columnNumber - 1;
					num3 += m_Offset.x;
					num = 1848677090;
					continue;
				case 11u:
				{
					int num18;
					int num19;
					if (m_Corner == Corner.BottomRight)
					{
						num18 = 399934562;
						num19 = num18;
					}
					else
					{
						num18 = 268390978;
						num19 = num18;
					}
					num = num18 ^ ((int)num2 * -536356094);
					continue;
				}
				case 6u:
				{
					IndexToCoordinates(index, out rowNumber, out columnNumber);
					num3 = 0f;
					num4 = 0f;
					int num13;
					int num14;
					if (m_Corner == Corner.BottomRight)
					{
						num13 = -878675233;
						num14 = num13;
					}
					else
					{
						num13 = -1232180847;
						num14 = num13;
					}
					num = num13 ^ (int)(num2 * 112657505);
					continue;
				}
				case 13u:
				{
					int num7;
					if (m_Corner == Corner.TopRight)
					{
						num = 1981367490;
						num7 = num;
					}
					else
					{
						num = 619740465;
						num7 = num;
					}
					continue;
				}
				case 12u:
				{
					int num9;
					int num10;
					if (!float.IsInfinity(num4))
					{
						num9 = -1357826612;
						num10 = num9;
					}
					else
					{
						num9 = -1770102691;
						num10 = num9;
					}
					num = num9 ^ ((int)num2 * -481370752);
					continue;
				}
				case 1u:
					rowNumber = m_Rows - rowNumber - 1;
					num4 += m_Offset.y;
					num = 1957604873;
					continue;
				case 15u:
				{
					int num17;
					if (m_Corner == Corner.Centered)
					{
						num = 1924939821;
						num17 = num;
					}
					else
					{
						num = 1390373795;
						num17 = num;
					}
					continue;
				}
				case 14u:
					num3 = 0f;
					num = 2041268845;
					continue;
				case 4u:
					num4 += m_Area.Height * 0.5f - m_Padding.Top - (float)rowNumber * m_Size.y - m_Spacing.Vertical * (float)rowNumber;
					num = (int)((num2 * 873998852) ^ 0x35C5835E);
					continue;
				case 8u:
				{
					int num8;
					if (!float.IsNaN(num4))
					{
						num = 778923196;
						num8 = num;
					}
					else
					{
						num = 760797149;
						num8 = num;
					}
					continue;
				}
				case 0u:
				{
					int num20;
					int num21;
					if (HasFixedSize)
					{
						num20 = 1606067794;
						num21 = num20;
					}
					else
					{
						num20 = 714742567;
						num21 = num20;
					}
					num = num20 ^ (int)(num2 * 1977398843);
					continue;
				}
				case 7u:
					num3 += (0f - m_Area.Width) * 0.5f + m_Padding.Left + m_Size.x * (float)columnNumber + m_Spacing.Horizontal * (float)columnNumber;
					num = 519711150;
					continue;
				case 9u:
				{
					int num15;
					int num16;
					if (!float.IsNaN(num3))
					{
						num15 = 794063306;
						num16 = num15;
					}
					else
					{
						num15 = 1061759203;
						num16 = num15;
					}
					num = num15 ^ (int)(num2 * 1900390260);
					continue;
				}
				case 5u:
				{
					int num11;
					int num12;
					if (m_Corner != Corner.BottomLeft)
					{
						num11 = -1373132503;
						num12 = num11;
					}
					else
					{
						num11 = -929790986;
						num12 = num11;
					}
					num = num11 ^ ((int)num2 * -322443232);
					continue;
				}
				case 2u:
				{
					int num5;
					int num6;
					if (!float.IsInfinity(num3))
					{
						num5 = -838228497;
						num6 = num5;
					}
					else
					{
						num5 = -902871747;
						num6 = num5;
					}
					num = num5 ^ (int)(num2 * 1391075854);
					continue;
				}
				case 17u:
					num3 += (m_Area.Width - (float)m_Columns * m_Size.x - (float)Mathf.Max(m_Columns - 1, 0) * m_Spacing.Horizontal) * 0.5f;
					num4 -= (m_Area.Height - (float)m_Rows * m_Size.y - (float)Mathf.Max(m_Rows - 1, 0) * m_Spacing.Vertical) * 0.5f;
					num = ((int)num2 * -457217607) ^ 0x7320D59C;
					continue;
				case 3u:
					num4 = 0f;
					num = 339182156;
					continue;
				default:
					offset = new Offset(num3, num4 - m_Size.y, num3 + m_Size.x, num4);
					return;
				}
				break;
			}
		}
	}

	protected void IndexToCoordinates(int index, out int rowNumber, out int columnNumber)
	{
		if (m_Axis == Axis.Horizontal)
		{
			rowNumber = (index != 0) ? Mathf.FloorToInt((float)index / (float)m_Columns) : 0;
			columnNumber = index - rowNumber * m_Columns;
		}
		else
		{
			columnNumber = (index != 0) ? Mathf.FloorToInt((float)index / (float)m_Rows) : 0;
			rowNumber = index - columnNumber * m_Rows;
		}
	}
}
