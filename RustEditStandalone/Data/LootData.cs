using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace RustEditStandalone.Data;

[Serializable]
[XmlRoot("SerializedLootableContainerData")]
public class SerializedLootableContainerData
{
    [XmlElement("entities")]
    public List<LootableContainerData> entities = new();
}

[Serializable]
public class LootableContainerData
{
    [XmlElement("filename")]
    public string filename = string.Empty;

    [XmlElement("items")]
    public List<LootableItemData> items = new();

    [XmlElement("respawnRateMin")]
    public int respawnRateMin = 1;

    [XmlElement("respawnRateMax")]
    public int respawnRateMax = 1;

    [XmlElement("refreshRateMin")]
    public int refreshRateMin = 1;

    [XmlElement("refreshRateMax")]
    public int refreshRateMax = 1;

    [XmlElement("spawnAmountMin")]
    public int spawnAmountMin = 1;

    [XmlElement("spawnAmountMax")]
    public int spawnAmountMax = 1;
}

[Serializable]
public class LootableItemData
{
    [XmlElement("shortname")]
    public string shortname;

    [XmlElement("minimum")]
    public int minimum;

    [XmlElement("maximum")]
    public int maximum;

    [XmlElement("blueprint")]
    public bool blueprint;
}
