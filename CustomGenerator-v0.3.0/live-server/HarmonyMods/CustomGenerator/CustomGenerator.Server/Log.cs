using UnityEngine;

namespace CustomGenerator.Server;

internal static class Log
{
	public static void Info(string message)
	{
		Debug.Log((object)("[CGen Server] " + message));
	}

	public static void Warning(string message)
	{
		Debug.LogWarning((object)("[CGen Server] " + message));
	}
}
