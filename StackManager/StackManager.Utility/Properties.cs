using System.Reflection;

namespace StackManager.Utility;

public class Properties
{
	public static string GetProduct()
	{
		try
		{
			return ((AssemblyProductAttribute)typeof(Properties).Assembly.GetCustomAttributes(typeof(AssemblyProductAttribute), inherit: true)[0]).Product;
		}
		catch
		{
			return string.Empty;
		}
	}

	public static string GetCopyright()
	{
		try
		{
			return ((AssemblyCopyrightAttribute)typeof(Properties).Assembly.GetCustomAttributes(typeof(AssemblyCopyrightAttribute), inherit: true)[0]).Copyright;
		}
		catch
		{
			return string.Empty;
		}
	}

	public static string GetVersion()
	{
		try
		{
			return ((AssemblyInformationalVersionAttribute)typeof(Properties).Assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), inherit: true)[0]).InformationalVersion;
		}
		catch
		{
			return "Unknown";
		}
	}
}
