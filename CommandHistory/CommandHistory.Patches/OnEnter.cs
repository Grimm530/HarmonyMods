using System.Collections.Generic;
using HarmonyLib;
using Windows;

namespace CommandHistory.Patches;

[HarmonyPatch(typeof(ConsoleInput))]
[HarmonyPatch("OnEnter")]
public static class OnEnter
{
	private static LinkedList<string> history = new LinkedList<string>();

	private static int count = 0;

	private static LinkedListNode<string> lastSelected = null;

	public static void Add(string value)
	{
		if (value.Length == 0)
		{
			return;
		}
		if (history.First?.Value == value)
		{
			lastSelected = history.First;
			return;
		}
		if (count > 200)
		{
			history.AddFirst(value);
			history.RemoveLast();
		}
		else
		{
			history.AddFirst(value);
			count++;
		}
		lastSelected = history.First;
	}

	public static string GetUp()
	{
		if (count == 0)
		{
			return null;
		}
		string value = lastSelected.Value;
		if (lastSelected.Next != null)
		{
			lastSelected = lastSelected.Next;
		}
		return value;
	}

	public static string GetDown()
	{
		if (count == 0)
		{
			return null;
		}
		string value = lastSelected.Value;
		if (lastSelected.Previous != null)
		{
			lastSelected = lastSelected.Previous;
		}
		return value;
	}

	private static void Prefix(ConsoleInput __instance)
	{
		Add(__instance.inputString);
	}
}
