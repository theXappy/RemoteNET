namespace ScubaDiver.Rtti;
public abstract class TypeInfo
{
    public static TypeInfo Dummy = new SecondClassTypeInfo("DummyModule", "DummyNamespace", "DummyType");

    public string ModuleName { get; }
    public string Namespace { get; }
    public string Name { get; }
    public string NamespaceAndName => string.IsNullOrWhiteSpace(Namespace) ? $"{Name}" : $"{Namespace}::{Name}";
    public string FullTypeName => $"{ModuleName}!{NamespaceAndName}";

    protected TypeInfo(string moduleName, string @namespace, string name)
    {
        ModuleName = moduleName;
        if (!string.IsNullOrWhiteSpace(@namespace))
            Namespace = @namespace;
        Name = name;
    }
}
