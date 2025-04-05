namespace ScubaDiver.Rtti;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Information about a "Second-Class Type" - Types which don't have a full RTTI entry and, most importantly, a vftable.
/// The existence of these types is inferred from their exported functions.
/// If none of the type's methods are exported, we might not know such a type even exists.
/// </summary>
public class SecondClassTypeInfo : TypeInfo
{
    public SecondClassTypeInfo(string moduleName, string @namespace, string name) : base(moduleName, @namespace, name)
    {
    }

    public override string ToString()
    {
        return $"{FullTypeName} (Second Class Type)";
    }
}
