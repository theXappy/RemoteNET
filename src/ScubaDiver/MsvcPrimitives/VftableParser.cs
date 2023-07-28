using System.Collections.Generic;
using ScubaDiver.Rtti;

public static class VftableParser
{
    public static List<FunctionInfo> Parse(TypeInfo typeInfo, Dictionary<nuint, string> mangledExports)
    {
        List<FunctionInfo> outputs = new List<FunctionInfo>();

        RttiScanner scanner = new RttiScanner();
        nuint offset = typeInfo.Address;
        while (true)
        {
            bool succ = scanner.TryRead((ulong)offset, out nuint funcAddr);
            if (!succ || !mangledExports.TryGetValue(funcAddr, out string funcName))
                break;

            FunctionInfo func = new FunctionInfo(funcName, funcAddr);
            outputs.Add(func);
        }

        return outputs;
    }
}