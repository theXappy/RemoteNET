using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScubaDiver.Rtti;

public class RichModuleInfo
{
    public ModuleInfo ModuleInfo { get; }
    public IReadOnlyList<ModuleSection> Sections { get; }
    public RichModuleInfo(ModuleInfo moduleInfo, IReadOnlyList<ModuleSection> sections)
    {
        ModuleInfo = moduleInfo;
        Sections = sections;
    }

    //public IEnumerable<ModuleSection> GetRttiRelevantSections()
    //{
    //    return Sections.Where(s => s.Name.Contains("DATA") || s.Name.Contains("RTTI"));
    //}

    public IEnumerable<ModuleSection> GetSections(Func<ModuleSection, bool> predicate)
    {
        return Sections.Where(predicate);
    }

    internal IEnumerable<ModuleSection> GetSections(string uppercaseName)
    {
        return Sections.Where(s => s.Name.ToUpper().Contains(uppercaseName.ToUpper()));
    }

    public override string ToString()
    {
        StringBuilder sb = new();
        sb.AppendLine(ModuleInfo.ToString());
        foreach (ModuleSection section in Sections)
        {
            sb.AppendLine(section.ToString());
        }
        return sb.ToString();
    }

}
