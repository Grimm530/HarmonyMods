using UnityEngine;

namespace StackManager.Utility;

public class Log
{
	public static void None(object message)
	{
		Debug.Log((object)$"{message}");
	}

	public static void Information(object message)
	{
		ConsoleWrite($"[INFO] {message}");
	}

	public static void Warning(object message)
	{
		ConsoleWrite($"[WARN] {message}");
	}

	public static void Error(object message)
	{
		ConsoleWrite($"[ERROR] {message}");
	}

	private static void ConsoleWrite(object message)
	{
		Debug.Log((object)$"[{Properties.GetProduct()}] {message}");
	}
}
