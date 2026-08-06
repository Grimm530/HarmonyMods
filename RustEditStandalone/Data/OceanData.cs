using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace RustEditStandalone.Data;

[Serializable]
[XmlRoot("SerializedPathList")]
public class SerializedPathList
{
    [XmlElement("vectorData")]
    public List<Vector3Data> vectorData = new();
}
