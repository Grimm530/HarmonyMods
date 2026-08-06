using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace RustEditStandalone.Data;

[Serializable]
[XmlRoot("SerializedVehicleData")]
public class SerializedVehicleData
{
    [XmlElement("vehicles")]
    public List<VehiclePrefabData> vehicles = new();
}

[Serializable]
public class VehiclePrefabData
{
    [XmlElement("id")]
    public uint id;

    [XmlElement("category")]
    public string category;

    [XmlElement("position")]
    public Vector3Data position;

    [XmlElement("rotation")]
    public Vector3Data rotation;
}
