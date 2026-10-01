using System.Collections.Generic;

namespace CustomGenerator.Shared;

public sealed class IOEntityInfo
{
	public string Prefab;

	public float[] Position;

	public List<IOConnectionInfo> Inputs = new List<IOConnectionInfo>();

	public List<IOConnectionInfo> Outputs = new List<IOConnectionInfo>();

	public int AccessLevel;

	public int DoorEffect = -1;

	public float TimerLength;

	public int Frequency;

	public bool UnlimitedAmmo;

	public bool PeaceKeeper;

	public string AutoTurretWeapon;

	public int BranchAmount;

	public int TargetCounterNumber;

	public string RcIdentifier;

	public bool CounterPassthrough;

	public int Floors = 1;

	public string PhoneName;
}
