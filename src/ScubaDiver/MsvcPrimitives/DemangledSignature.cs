namespace ScubaDiver;

public class DemangledSignature
{
    public static DemangledSignature Empty = new DemangledSignature();

    public string[] ArgTypes { get; set; }
    public string RetType { get; set; }
    // If the return value is a "real" struct (not a pointer to a struct)
    public bool IsRetNonRefStruct { get; set; }
}