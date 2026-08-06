using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace RustEditStandalone.Data;

[Serializable]
[XmlRoot("SerializedAPCPathList")]
public class SerializedApcPathList
{
    [XmlElement("paths")]
    public List<SerializedApcPath> paths = new();
}

[Serializable]
public class SerializedApcPath
{
    [XmlElement("nodes")]
    public List<Vector3Data> nodes = new();

    [XmlElement("interestNodes")]
    public List<Vector3Data> interestNodes = new();
}
