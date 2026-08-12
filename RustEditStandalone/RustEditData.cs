using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace RustEditStandalone;

/// <summary>
/// RustEdit vending data - must match format written by RustEdit editor.
/// Uses XML serialization (same as Oxide.Ext.RustEdit).
/// </summary>
[Serializable]
[XmlRoot("SerializedVendingContainerData")]
public class SerializedVendingContainerData
{
    [XmlElement("entities")]
    public List<VendingContainerData> Entities { get; set; } = new();
}

[Serializable]
public class VendingContainerData
{
    [XmlElement("filename")]
    public string Filename { get; set; } = string.Empty;

    [XmlElement("items")]
    public List<VendingItemData> Items { get; set; } = new();
}

[Serializable]
public class VendingItemData
{
    [XmlElement("sellItemShortname")]
    public string SellItemShortname { get; set; }

    [XmlElement("sellItemAmount")]
    public int SellItemAmount { get; set; }

    [XmlElement("sellItemBlueprint")]
    public bool SellItemBlueprint { get; set; }

    [XmlElement("currencyItemShortname")]
    public string CurrencyItemShortname { get; set; }

    [XmlElement("currencyItemAmount")]
    public int CurrencyItemAmount { get; set; }

    [XmlElement("currencyItemBlueprint")]
    public bool CurrencyItemBlueprint { get; set; }

    [XmlElement("weight")]
    public int Weight { get; set; }
}
