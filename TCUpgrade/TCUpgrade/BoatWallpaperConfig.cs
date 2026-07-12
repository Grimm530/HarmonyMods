using UnityEngine;

namespace TCUpgrade;

public class BoatWallpaperConfig
{
	public BaseEntity Boat;

	public ulong WallpaperId = 1uL;

	public string Category = "Wall";

	public int Page;

	public bool WpInternal = true;

	public bool WpExternal;

	public Coroutine WorkWallpaper;

	public bool Work;
}
