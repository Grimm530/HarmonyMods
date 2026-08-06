using System;
using System.Xml.Serialization;
using UnityEngine;

namespace RustEditStandalone.Data;

[Serializable]
public class Vector3Data
{
    [XmlElement("x")]
    public float x;

    [XmlElement("y")]
    public float y;

    [XmlElement("z")]
    public float z;

    public Vector3 ToVector3() => new(x, y, z);

    public static implicit operator Vector3(Vector3Data v) => v?.ToVector3() ?? Vector3.zero;
}
