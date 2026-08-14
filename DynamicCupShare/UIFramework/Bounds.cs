using Unity.Mathematics;

namespace Oxide.Ext.Chaos.UIFramework;

public struct Bounds
{
	public readonly float X;

	public readonly float Y;

	private string m_String;

	public static readonly Bounds zero = new Bounds(0f, 0f);

	public static readonly Bounds one = new Bounds(1f, 1f);

	public Bounds(float x, float y)
	{
		X = x;
		Y = y;
		m_String = $"{X} {Y}";
	}

	public Bounds(float2 distance)
	{
		X = distance.x;
		Y = distance.y;
		m_String = $"{X} {Y}";
	}

	public string ToString(string defaultValue)
	{
		if (string.IsNullOrEmpty(m_String))
		{
			return defaultValue;
		}
		return m_String;
	}

	public override string ToString()
	{
		return $"{X} {Y}";
	}

	public override bool Equals(object obj)
	{
		if (!(obj is Bounds bounds))
		{
			return false;
		}
		if (bounds.X.Equals(X))
		{
			return bounds.Y.Equals(Y);
		}
		return false;
	}

	public bool Equals(Bounds other)
	{
		if (X.Equals(other.X))
		{
			return Y.Equals(other.Y);
		}
		return false;
	}

	public override int GetHashCode()
	{
		return (X.GetHashCode() * 397) ^ Y.GetHashCode();
	}

	public static bool operator ==(Bounds lhs, Bounds rhs)
	{
		return lhs.Equals(rhs);
	}

	public static bool operator !=(Bounds lhs, Bounds rhs)
	{
		return !lhs.Equals(rhs);
	}

	public static Bounds operator +(Bounds a, Bounds b)
	{
		return new Bounds(a.X + b.X, a.Y + b.Y);
	}

	public static Bounds operator -(Bounds a, Bounds b)
	{
		return new Bounds(a.X - b.X, a.Y - b.Y);
	}

	public static Bounds operator /(Bounds a, Bounds b)
	{
		return new Bounds(a.X / b.X, a.Y / b.Y);
	}

	public static Bounds operator *(Bounds a, Bounds b)
	{
		return new Bounds(a.X * b.X, a.Y * b.Y);
	}

	public static Bounds operator /(Bounds a, float f)
	{
		return new Bounds(a.X / f, a.Y / f);
	}

	public static Bounds operator *(Bounds a, float f)
	{
		return new Bounds(a.X * f, a.Y * f);
	}
}
