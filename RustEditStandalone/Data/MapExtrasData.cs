using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace RustEditStandalone.Data;

/// <summary>
/// JSON map layer named "customgenerator". Same shape the live-server IO connector reads:
/// an IO list of prefab, position, inputs/outputs, and per-entity settings.
/// </summary>
public sealed class MapExtrasData
{
    public const string LayerName = "customgenerator";
    public const int KnownVersion = 1;

    public int Version = 1;
    public List<MapExtrasIoEntity> IO = new();

    public static bool TryFromBytes(byte[] data, out MapExtrasData result, out string error)
    {
        result = null;
        error = null;
        if (data == null || data.Length == 0)
        {
            error = "empty";
            return false;
        }

        try
        {
            string json = Encoding.UTF8.GetString(data).TrimStart('\ufeff');
            result = JsonConvert.DeserializeObject<MapExtrasData>(json);
            if (result == null)
            {
                error = "empty JSON";
                return false;
            }
            if (result.IO == null)
                result.IO = new List<MapExtrasIoEntity>();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}

public sealed class MapExtrasIoEntity
{
    public string Prefab;
    public float[] Position;
    public List<MapExtrasIoConnection> Inputs = new();
    public List<MapExtrasIoConnection> Outputs = new();
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

public sealed class MapExtrasIoConnection
{
    public string Prefab;
    public float[] Position;
    public int Slot;
    public int Type;
}
