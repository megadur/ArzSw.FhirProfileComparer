using System.Collections.Generic;

namespace ArzSw.FhirProfileComparer.Api.Models;

public class CompareResult
{
    public string PackageId { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public string TargetVersion { get; set; } = string.Empty;
    public string TargetChangelog { get; set; } = string.Empty;
    public List<ProfileDelta> Profiles { get; set; } = new();
}

public class ProfileDelta
{
    public string ProfileUrl { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    
    public bool IsNew { get; set; }
    public bool IsRemoved { get; set; }
    
    public List<string> ProfilePaths { get; set; } = new();
    
    public List<ElementDelta> AddedElements { get; set; } = new();
    public List<ElementDelta> RemovedElements { get; set; } = new();
    public List<ElementDelta> ModifiedElements { get; set; } = new();
}

public class ElementDelta
{
    public string Path { get; set; } = string.Empty;
    public string SliceName { get; set; } = string.Empty;
    public string Property { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}
