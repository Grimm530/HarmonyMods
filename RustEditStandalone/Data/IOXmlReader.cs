using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;

namespace RustEditStandalone.Data;

/// <summary>
/// RustEdit saves IO as XML <see cref="SerializedIOData"/> on layers such as "io" and "rustedit_io".
/// </summary>
public static class IOXmlReader
{
    public static bool LooksLikeXml(byte[] data)
    {
        if (data == null || data.Length < 5) return false;
        int i = 0;
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            i = 3;
        while (i < data.Length && (data[i] == (byte)' ' || data[i] == (byte)'\n' || data[i] == (byte)'\r' || data[i] == (byte)'\t'))
            i++;
        return i < data.Length && data[i] == (byte)'<';
    }

    public static bool TryRead(byte[] data, out SerializedIOData result)
    {
        result = null;
        if (!LooksLikeXml(data)) return false;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(Encoding.UTF8.GetString(data).TrimStart('\ufeff'));
            if (doc.DocumentElement == null || doc.DocumentElement.Name != "SerializedIOData")
                return false;

            var parsed = new SerializedIOData();
            var nodes = doc.DocumentElement.SelectNodes("entities/SerializedIOEntity");
            if (nodes != null)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (nodes[i] is XmlElement element)
                        parsed.entities.Add(ReadEntity(element));
                }
            }
            result = parsed;
            return parsed.entities.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static SerializedIOEntity ReadEntity(XmlElement e)
    {
        return new SerializedIOEntity
        {
            fullPath = Text(e, "fullPath"),
            position = ReadVector(e["position"]),
            inputs = ReadConnections(e["inputs"]),
            outputs = ReadConnections(e["outputs"]),
            accessLevel = Int(e, "accessLevel"),
            doorEffect = Int(e, "doorEffect", -1),
            timerLength = Float(e, "timerLength"),
            frequency = Int(e, "frequency"),
            unlimitedAmmo = Bool(e, "unlimitedAmmo"),
            peaceKeeper = Bool(e, "peaceKeeper"),
            autoTurretWeapon = Text(e, "autoTurretWeapon"),
            branchAmount = Int(e, "branchAmount"),
            targetCounterNumber = Int(e, "targetCounterNumber"),
            rcIdentifier = Text(e, "rcIdentifier"),
            counterPassthrough = Bool(e, "counterPassthrough"),
            floors = Int(e, "floors", 1),
            phoneName = Text(e, "phoneName")
        };
    }

    private static SerializedConnectionData[] ReadConnections(XmlElement list)
    {
        if (list == null) return null;
        var links = new List<SerializedConnectionData>();
        foreach (XmlNode node in list.ChildNodes)
        {
            if (node is not XmlElement item) continue;
            string path = Text(item, "fullPath");
            if (string.IsNullOrEmpty(path))
            {
                links.Add(null);
                continue;
            }
            links.Add(new SerializedConnectionData
            {
                fullPath = path,
                position = ReadVector(item["position"]),
                connectedTo = Int(item, "connectedTo"),
                type = Int(item, "type"),
                input = Bool(item, "input")
            });
        }
        return links.Count > 0 ? links.ToArray() : null;
    }

    private static IOVectorData ReadVector(XmlElement v)
    {
        if (v == null) return default;
        return new IOVectorData
        {
            x = Float(v, "x"),
            y = Float(v, "y"),
            z = Float(v, "z")
        };
    }

    private static string Text(XmlElement e, string name)
    {
        string text = e[name]?.InnerText;
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static int Int(XmlElement e, string name, int fallback = 0)
    {
        return int.TryParse(Text(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : fallback;
    }

    private static float Float(XmlElement e, string name)
    {
        return float.TryParse(Text(e, name), NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            ? result
            : 0f;
    }

    private static bool Bool(XmlElement e, string name)
    {
        return Text(e, name) == "true";
    }
}
