using System.Collections.Generic;
using Newtonsoft.Json;

namespace Oxide.Ext.Chaos.UIFramework;

public class CountdownComponent : BaseCuiComponent, ICuiCommandComponent
{
	public enum TimerFormat
	{
		None,
		SecondsHundreth,
		MinutesSeconds,
		MinutesSecondsHundreth,
		HoursMinutes,
		HoursMinutesSeconds,
		HoursMinutesSecondsMilliseconds,
		HoursMinutesSecondsTenths,
		DaysHoursMinutes,
		DaysHoursMinutesSeconds,
		Custom
	}

	public int EndTime { get; set; }
	public int StartTime { get; set; }
	public int Step { get; set; } = 1;
	public string Command { get; set; }
	public TimerFormat Format { get; set; }
	public string NumberFormat { get; set; } = "0.####";
	public bool DestroyIfDone { get; set; } = true;

	public CountdownComponent() { }

	public CountdownComponent(int startTime = 0, int endTime = 0, int step = 1, string command = "")
	{
		StartTime = startTime;
		EndTime = endTime;
		Step = step;
		Command = command;
	}

	public void SetCommand(CommandCallbackHandler commandCallbackHandler, System.Action<ConsoleSystem.Arg> callback, string identifier = "")
	{
		Command = commandCallbackHandler.RegisterCommand(callback, null, identifier);
	}

	void ICuiCommandComponent.SetCommand(CommandCallbackHandler commandCallbackHandler, System.Action<ConsoleSystem.Arg> callback, string identifier)
	{
		SetCommand(commandCallbackHandler, callback, identifier);
	}

	public override void CopyFrom<T>(T other)
	{
		if (other is CountdownComponent c)
		{
			EndTime = c.EndTime;
			StartTime = c.StartTime;
			Step = c.Step;
			Command = c.Command;
			Format = c.Format;
			NumberFormat = c.NumberFormat;
			DestroyIfDone = c.DestroyIfDone;
		}
	}

	public override void WriteJson(JsonWriter jsonWriter, List<string> dirtyFields)
	{
		jsonWriter.WriteStartObject();
		jsonWriter.WritePropertyName("type");
		jsonWriter.WriteValue("UnityEngine.UI.Countdown");
		jsonWriter.WritePropertyName("endTime");
		jsonWriter.WriteValue(EndTime);
		jsonWriter.WritePropertyName("startTime");
		jsonWriter.WriteValue(StartTime);
		jsonWriter.WritePropertyName("step");
		jsonWriter.WriteValue(Step);
		if (!string.IsNullOrEmpty(Command))
		{
			jsonWriter.WritePropertyName("command");
			jsonWriter.WriteValue(Command);
		}
		jsonWriter.WritePropertyName("timerFormat");
		jsonWriter.WriteValue(Format.ToString());
		jsonWriter.WritePropertyName("numberFormat");
		jsonWriter.WriteValue(NumberFormat);
		jsonWriter.WritePropertyName("destroyIfDone");
		jsonWriter.WriteValue(DestroyIfDone);
		jsonWriter.WriteEndObject();
	}

	public override void OnEnterPool()
	{
		EndTime = 0;
		StartTime = 0;
		Step = 1;
		Command = null;
		Format = TimerFormat.None;
		NumberFormat = "0.####";
		DestroyIfDone = true;
	}
}
