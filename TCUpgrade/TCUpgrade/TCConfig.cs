using UnityEngine;

namespace TCUpgrade;

public class TCConfig
{
	public int Id;

	public BuildingGrade.Enum Grade = BuildingGrade.Enum.Wood;

	public int SkinId;

	public bool Color;

	public uint Colour;

	public Coroutine WorkUpgrade;

	public Coroutine WorkRepair;

	public Coroutine WorkReskin;

	public Coroutine WorkWallpaper;

	public Coroutine WorkUpwall;

	public bool Work;

	public bool Repair;

	public bool Reskin;

	public bool Upwall;

	public bool Effect = true;

	public bool Downgrade;

	public ulong WallpaperId = 1uL;

	public bool Wallpall;

	public bool WpInternal = true;

	public bool WpExternal;

	public ulong Player;
}
