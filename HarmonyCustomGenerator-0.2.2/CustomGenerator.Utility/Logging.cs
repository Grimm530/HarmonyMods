using System;
using System.IO;
using UnityEngine;

namespace CustomGenerator.Utility;

public static class Logging
{
	private static readonly string LogFolder;

	private static readonly string LogFile;

	private static bool isInitialized;

	static Logging()
	{
		LogFolder = "HarmonyConfig/logs";
		try
		{
			if (!Directory.Exists(LogFolder))
			{
				Directory.CreateDirectory(LogFolder);
			}
			LogFile = Path.Combine(LogFolder, $"cgen_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
			File.WriteAllText(LogFile, $"=== CustomGenerator Log Started at {DateTime.Now} ===\n");
			isInitialized = true;
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("[CGen Logger] Failed to initialize: " + ex.Message));
			isInitialized = false;
		}
	}

	public static void StartingMessage()
	{
		Info("CustomGenerator by [aristocratos]");
		Debug.Log((object)new string('-', 30));
		Debug.Log((object)"USE ONLY FOR MAP GENERATING!");
		Debug.Log((object)"NOT FOR LIVE SERVER!!!");
		Debug.Log((object)("Config version: " + ExtConfig.Config.Version));
		Debug.Log((object)new string('-', 30));
	}

	public static void Info(string message)
	{
		string message2 = $"[INFO] {DateTime.Now:HH:mm:ss} | {message}";
		Debug.Log((object)("[CGen] " + message));
		WriteToFile(message2);
	}

	public static void Dbg(string message)
	{
		string message2 = $"[DEBUG] {DateTime.Now:HH:mm:ss} | {message}";
		Debug.Log((object)("[CGen|Debug] " + message));
		WriteToFile(message2);
	}

	public static void Warning(string message)
	{
		string message2 = $"[WARN] {DateTime.Now:HH:mm:ss} | {message}";
		Debug.LogWarning((object)("[CGen] " + message));
		WriteToFile(message2);
	}

	public static void Error(string message, Exception ex = null)
	{
		string text = $"[ERROR] {DateTime.Now:HH:mm:ss} | {message}";
		if (ex != null)
		{
			text += $"\n{ex.GetType()}: {ex.Message}\n{ex.StackTrace}";
		}
		Debug.LogError((object)("[CGen] " + message));
		WriteToFile(text);
	}

	public static void Generation(string message)
	{
		string message2 = $"[GEN] {DateTime.Now:HH:mm:ss} | {message}";
		Debug.Log((object)("[CGen Gen] " + message));
		WriteToFile(message2);
	}

	public static void Config(string message)
	{
		string message2 = $"[CFG] {DateTime.Now:HH:mm:ss} | {message}";
		Debug.Log((object)("[CGen Config] " + message));
		WriteToFile(message2);
	}

	private static void WriteToFile(string message)
	{
		if (!isInitialized)
		{
			return;
		}
		try
		{
			File.AppendAllText(LogFile, message + "\n");
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("[CGen Logger] Failed to write to log file: " + ex.Message));
		}
	}

	public static void ClearOldLogs(int daysToKeep = 2)
	{
		try
		{
			if (!Directory.Exists(LogFolder))
			{
				return;
			}
			string[] files = Directory.GetFiles(LogFolder, "cgen_*.log");
			DateTime dateTime = DateTime.Now.AddDays(-daysToKeep);
			string[] array = files;
			for (int i = 0; i < array.Length; i++)
			{
				FileInfo fileInfo = new FileInfo(array[i]);
				if (fileInfo.CreationTime < dateTime)
				{
					fileInfo.Delete();
				}
			}
		}
		catch (Exception ex)
		{
			Debug.LogError((object)("[CGen Logger] Failed to clear old logs: " + ex.Message));
		}
	}
}
