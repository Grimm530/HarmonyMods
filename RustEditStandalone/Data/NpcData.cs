using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace RustEditStandalone.Data;

public enum NpcType
{
    Scientist = 0,
    Peacekeeper = 1,
    HeavyScientist = 2,
    JunkpileScientist = 3,
    Bandit = 4,
    Murderer = 5,
    Scarecrow = 6
}

[Serializable]
[XmlRoot("SerializedNPCData")]
public class SerializedNpcData
{
    [XmlElement("npcSpawners")]
    public List<SerializedNpcSpawner> npcSpawners = new();
}

[Serializable]
public class SerializedNpcSpawner
{
    [XmlElement("npcType")]
    public int npcType;

    [XmlElement("respawnMin")]
    public int respawnMin;

    [XmlElement("respawnMax")]
    public int respawnMax;

    [XmlElement("position")]
    public Vector3Data position;

    [XmlElement("category")]
    public string category;
}
