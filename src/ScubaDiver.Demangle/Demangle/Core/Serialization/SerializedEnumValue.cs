using System.Xml.Serialization;

namespace ScubaDiver.Demangle.Demangle.Core.Serialization;

public class SerializedEnumValue
{
    [XmlAttribute("name")]
    public string? Name;
    [XmlAttribute("value")]
    public int Value;
}