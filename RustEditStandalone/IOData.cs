using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RustEditStandalone;

/// <summary>
/// RustEdit IO layer data - plain DTOs matching Oxide.Ext.RustEdit.IO schema.
/// Deserialized manually from ProtoBuf wire format to avoid loading protobuf-net at runtime.
/// </summary>

public class SerializedIOData
{
    public List<SerializedIOEntity> entities { get; set; } = new List<SerializedIOEntity>();
}

public class SerializedIOEntity
{
    public string fullPath { get; set; }
    public IOVectorData position { get; set; }
    public SerializedConnectionData[] inputs { get; set; }
    public SerializedConnectionData[] outputs { get; set; }
    public int accessLevel { get; set; }
    public int doorEffect { get; set; }
    public float timerLength { get; set; }
    public int frequency { get; set; }
    public bool unlimitedAmmo { get; set; }
    public bool peaceKeeper { get; set; }
    public string autoTurretWeapon { get; set; }
    public int branchAmount { get; set; }
    public int targetCounterNumber { get; set; }
    public string rcIdentifier { get; set; }
    public bool counterPassthrough { get; set; }
    public int floors { get; set; } = 1;
    public string phoneName { get; set; }
}

public class SerializedConnectionData
{
    public string fullPath { get; set; }
    public IOVectorData position { get; set; }
    public bool input { get; set; }
    public int connectedTo { get; set; }
    public int type { get; set; }
}

public struct IOVectorData
{
    public float x, y, z;

    public Vector3 ToVector3() => new Vector3(x, y, z);

    public static IOVectorData FromVector3(Vector3 v) =>
        new IOVectorData { x = v.x, y = v.y, z = v.z };
}

/// <summary>
/// Manual ProtoBuf deserializer for SerializedIOData (no protobuf-net dependency at runtime).
/// </summary>
public static class IODataDeserializer
{
    private const int WireVarint = 0;
    private const int WireFixed64 = 1;
    private const int WireLengthDelimited = 2;
    private const int WireFixed32 = 5;

    public static bool TryDeserialize(byte[] data, out SerializedIOData result)
    {
        result = null;
        if (data == null || data.Length < 4) return false;
        try
        {
            using var ms = new MemoryStream(data);
            result = ReadSerializedIOData(ms);
            return result?.entities != null && result.entities.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static SerializedIOData ReadSerializedIOData(Stream s)
    {
        var data = new SerializedIOData();
        while (ReadTag(s, out int fieldNumber, out int wireType))
        {
            if (fieldNumber == 1 && wireType == WireLengthDelimited)
            {
                var entity = ReadSerializedIOEntity(ReadLengthDelimited(s));
                if (entity != null)
                    data.entities.Add(entity);
            }
            else
                SkipField(s, wireType);
        }
        return data;
    }

    private static SerializedIOEntity ReadSerializedIOEntity(byte[] chunk)
    {
        if (chunk == null || chunk.Length == 0) return null;
        var e = new SerializedIOEntity();
        using var ms = new MemoryStream(chunk);
        var inputs = new List<SerializedConnectionData>();
        var outputs = new List<SerializedConnectionData>();
        while (ReadTag(ms, out int fieldNumber, out int wireType))
        {
            switch (fieldNumber)
            {
                case 1:
                    if (wireType == WireLengthDelimited)
                        ReadField1LengthDelimited(ms, e);
                    else
                        SkipField(ms, wireType);
                    break;
                case 2:
                    if (wireType == WireLengthDelimited)
                        ReadField2LengthDelimited(ms, e);
                    else
                        SkipField(ms, wireType);
                    break;
                case 3: var inp = ReadConnection(ms, wireType); if (inp != null) inputs.Add(inp); break;
                case 4: var outp = ReadConnection(ms, wireType); if (outp != null) outputs.Add(outp); break;
                case 5: e.accessLevel = ReadInt32(ms, wireType); break;
                case 6: e.doorEffect = ReadInt32(ms, wireType); break;
                case 7: e.timerLength = ReadFloat(ms, wireType); break;
                case 8: e.frequency = ReadInt32(ms, wireType); break;
                case 9: e.unlimitedAmmo = ReadBool(ms, wireType); break;
                case 10: e.peaceKeeper = ReadBool(ms, wireType); break;
                case 11: e.autoTurretWeapon = ReadString(ms, wireType); break;
                case 12: e.branchAmount = ReadInt32(ms, wireType); break;
                case 13: e.targetCounterNumber = ReadInt32(ms, wireType); break;
                case 14: e.rcIdentifier = ReadString(ms, wireType); break;
                case 15: e.counterPassthrough = ReadBool(ms, wireType); break;
                case 16: e.floors = ReadInt32(ms, wireType); break;
                case 17: e.phoneName = ReadString(ms, wireType); break;
                default: SkipField(ms, wireType); break;
            }
        }
        e.inputs = inputs.Count > 0 ? inputs.ToArray() : null;
        e.outputs = outputs.Count > 0 ? outputs.ToArray() : null;
        return e;
    }

    /// <summary>Field 1 can be fullPath (string) or position (message) depending on map format.</summary>
    private static void ReadField1LengthDelimited(Stream s, SerializedIOEntity e)
    {
        var chunk = ReadLengthDelimited(s);
        if (chunk == null) return;
        if (TryParseAsVector(chunk, out var pos))
            e.position = pos;
        else
            e.fullPath = System.Text.Encoding.UTF8.GetString(chunk);
    }

    /// <summary>Field 2 can be position (message) or fullPath (string) depending on map format.</summary>
    private static void ReadField2LengthDelimited(Stream s, SerializedIOEntity e)
    {
        var chunk = ReadLengthDelimited(s);
        if (chunk == null) return;
        if (TryParseAsVector(chunk, out var pos))
            e.position = pos;
        else
            e.fullPath = System.Text.Encoding.UTF8.GetString(chunk);
    }

    private static bool TryParseAsVector(byte[] chunk, out IOVectorData v)
    {
        v = default;
        if (chunk == null || chunk.Length < 12) return false;
        try
        {
            using var ms = new MemoryStream(chunk);
            float x = 0, y = 0, z = 0;
            while (ReadTag(ms, out int fn, out int wt))
            {
                if (fn == 1) x = ReadFloat(ms, wt);
                else if (fn == 2) y = ReadFloat(ms, wt);
                else if (fn == 3) z = ReadFloat(ms, wt);
                else SkipField(ms, wt);
            }
            v = new IOVectorData { x = x, y = y, z = z };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static SerializedConnectionData ReadConnection(Stream s, int wireType)
    {
        if (wireType != WireLengthDelimited) { SkipField(s, wireType); return null; }
        var chunk = ReadLengthDelimited(s);
        if (chunk == null || chunk.Length == 0) return null;
        var c = new SerializedConnectionData();
        using var ms = new MemoryStream(chunk);
        while (ReadTag(ms, out int fieldNumber, out int wt))
        {
            switch (fieldNumber)
            {
                case 1: c.fullPath = ReadString(ms, wt); break;
                case 2: c.position = ReadVector(ms, wt); break;
                case 3: c.input = ReadBool(ms, wt); break;
                case 4: c.connectedTo = ReadInt32(ms, wt); break;
                case 5: c.type = ReadInt32(ms, wt); break;
                default: SkipField(ms, wt); break;
            }
        }
        return c;
    }

    private static IOVectorData ReadVector(Stream s, int wireType)
    {
        if (wireType != WireLengthDelimited) { SkipField(s, wireType); return default; }
        var chunk = ReadLengthDelimited(s);
        if (chunk == null || chunk.Length < 12) return default;
        var v = default(IOVectorData);
        using var ms = new MemoryStream(chunk);
        while (ReadTag(ms, out int fieldNumber, out int wt))
        {
            if (fieldNumber == 1) v.x = ReadFloat(ms, wt);
            else if (fieldNumber == 2) v.y = ReadFloat(ms, wt);
            else if (fieldNumber == 3) v.z = ReadFloat(ms, wt);
            else SkipField(ms, wt);
        }
        return v;
    }

    private static bool ReadTag(Stream s, out int fieldNumber, out int wireType)
    {
        fieldNumber = 0;
        wireType = 0;
        int b = s.ReadByte();
        if (b < 0) return false;
        uint tag = (uint)b;
        if ((tag & 0x80) != 0)
        {
            int shift = 7;
            while (true)
            {
                b = s.ReadByte();
                if (b < 0) return false;
                tag |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
                if (shift > 35) return false;
            }
        }
        fieldNumber = (int)(tag >> 3);
        wireType = (int)(tag & 7);
        return true;
    }

    private static int ReadVarint(Stream s)
    {
        int result = 0;
        int shift = 0;
        while (true)
        {
            int b = s.ReadByte();
            if (b < 0) break;
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
            if (shift >= 35) return result;
        }
        return result;
    }

    private static byte[] ReadLengthDelimited(Stream s)
    {
        int len = ReadVarint(s);
        if (len <= 0 || len > 1024 * 1024) return null;
        var buf = new byte[len];
        int read = 0;
        while (read < len)
        {
            int n = s.Read(buf, read, len - read);
            if (n <= 0) return null;
            read += n;
        }
        return buf;
    }

    private static string ReadString(Stream s, int wireType)
    {
        if (wireType != WireLengthDelimited) { SkipField(s, wireType); return null; }
        var bytes = ReadLengthDelimited(s);
        return bytes != null ? System.Text.Encoding.UTF8.GetString(bytes) : null;
    }

    private static int ReadInt32(Stream s, int wireType)
    {
        if (wireType == WireVarint) return ReadVarint(s);
        SkipField(s, wireType);
        return 0;
    }

    private static bool ReadBool(Stream s, int wireType)
    {
        if (wireType == WireVarint) return ReadVarint(s) != 0;
        SkipField(s, wireType);
        return false;
    }

    private static float ReadFloat(Stream s, int wireType)
    {
        if (wireType == WireFixed32)
        {
            var buf = new byte[4];
            if (s.Read(buf, 0, 4) != 4) return 0f;
            return BitConverter.ToSingle(buf, 0);
        }
        SkipField(s, wireType);
        return 0f;
    }

    private static void SkipField(Stream s, int wireType)
    {
        switch (wireType)
        {
            case WireVarint:
                while (true)
                {
                    int b = s.ReadByte();
                    if (b < 0) return;
                    if ((b & 0x80) == 0) return;
                }
            case WireFixed64:
                s.Seek(8, SeekOrigin.Current);
                break;
            case WireLengthDelimited:
                int len = ReadVarint(s);
                if (len > 0 && len <= 1024 * 1024)
                    s.Seek(len, SeekOrigin.Current);
                break;
            case WireFixed32:
                s.Seek(4, SeekOrigin.Current);
                break;
        }
    }
}
