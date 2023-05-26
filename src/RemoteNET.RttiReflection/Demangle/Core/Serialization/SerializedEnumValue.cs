using System.Xml.Serialization;

namespace Reko.Core.Serialization;

public class SerializedEnumValue
{
    [XmlAttribute("name")]
    public string? Name;
    [XmlAttribute("value")]
    public int Value;
}