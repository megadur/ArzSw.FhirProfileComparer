using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using ArzSw.FhirProfileComparer.Api.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ArzSw.FhirProfileComparer.Api.Services;

public class ProfileDependencyEdge
{
    public string ParentUrl { get; set; } = string.Empty;
    public string ParentName { get; set; } = string.Empty;
    public string ChildUrl { get; set; } = string.Empty;
    public string ReferenceLabel { get; set; } = string.Empty;
}

public class ProfileComparerService
{
    private readonly ILogger<ProfileComparerService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _baseProfileDir;

    public ProfileComparerService(ILogger<ProfileComparerService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        _baseProfileDir = Path.Combine(Path.GetTempPath(), "fhir-profiles");
    }

    public async Task<CompareResult> CompareAsync(string packageId, string sourceVersion, string targetVersion)
    {
        _logger.LogInformation("Comparing {PackageId} {Source} vs {Target}", packageId, sourceVersion, targetVersion);

        var sourceDir = await DownloadAndExtractPackageAsync(packageId, sourceVersion);
        var targetDir = await DownloadAndExtractPackageAsync(packageId, targetVersion);

        var sourceProfiles = LoadStructureDefinitions(sourceDir);
        var targetProfiles = LoadStructureDefinitions(targetDir);

        var result = new CompareResult
        {
            PackageId = packageId,
            SourceVersion = sourceVersion,
            TargetVersion = targetVersion,
            TargetChangelog = await FetchChangelogAsync(packageId, sourceVersion, targetVersion)
        };

        var dependencyEdges = BuildDependencyEdges(targetProfiles);

        // Compare logic
        foreach (var targetProfile in targetProfiles)
        {
            var sourceProfile = sourceProfiles.FirstOrDefault(p => p.Url?.Split('|')[0] == targetProfile.Url?.Split('|')[0]);
            if (sourceProfile == null)
            {
                var paths = FindProfilePaths(targetProfile.Url?.Split('|')[0] ?? string.Empty, targetProfile.Name ?? targetProfile.Id ?? string.Empty, dependencyEdges, new HashSet<string>());
                result.Profiles.Add(new ProfileDelta { ProfileUrl = targetProfile.Url ?? string.Empty, ProfileName = targetProfile.Name ?? targetProfile.Id ?? string.Empty, IsNew = true, ProfilePaths = paths });
                continue;
            }

            var delta = CompareProfiles(sourceProfile, targetProfile);
            if (delta.AddedElements.Any() || delta.RemovedElements.Any() || delta.ModifiedElements.Any() || delta.IsNew || delta.IsRemoved)
            {
                delta.ProfilePaths = FindProfilePaths(targetProfile.Url?.Split('|')[0] ?? string.Empty, targetProfile.Name ?? targetProfile.Id ?? string.Empty, dependencyEdges, new HashSet<string>());
                result.Profiles.Add(delta);
            }
        }

        // Find removed profiles
        foreach (var sourceProfile in sourceProfiles)
        {
            var targetProfile = targetProfiles.FirstOrDefault(p => p.Url?.Split('|')[0] == sourceProfile.Url?.Split('|')[0]);
            if (targetProfile == null)
            {
                var removedUrl = sourceProfile.Url?.Split('|')[0] ?? string.Empty;
                var removedName = sourceProfile.Name ?? sourceProfile.Id ?? string.Empty;
                var paths = FindProfilePaths(removedUrl, removedName, dependencyEdges, new HashSet<string>());
                result.Profiles.Add(new ProfileDelta { ProfileUrl = sourceProfile.Url ?? string.Empty, ProfileName = removedName, IsRemoved = true, ProfilePaths = paths });
            }
        }

        return result;
    }

    public ProfileDelta CompareProfiles(StructureDefinition source, StructureDefinition target)
    {
        var delta = new ProfileDelta
        {
            ProfileUrl = target?.Url ?? string.Empty,
            ProfileName = target?.Name ?? target?.Id ?? string.Empty,   
        };

        var sourceElements = source?.Differential?.Element?.Where(e => e.Max != "0").ToList() ?? new();
        var targetElements = target?.Differential?.Element?.Where(e => e.Max != "0").ToList() ?? new();

        // Find added and modified
        foreach (var tElement in targetElements)
        {
            var sElement = sourceElements.FirstOrDefault(e => e.ElementId == tElement.ElementId);
            if (sElement == null)
            {
                delta.AddedElements.Add(new ElementDelta { Path = tElement.Path, SliceName = tElement.SliceName ?? "", Type = tElement.Type?.FirstOrDefault()?.Code ?? "" });
            }
            else
            {
                // Check for modifications (e.g., Min/Max cardinality)
                if (sElement.Min != tElement.Min || sElement.Max != tElement.Max)
                {
                    delta.ModifiedElements.Add(new ElementDelta
                    {
                        Path = tElement.Path,
                        SliceName = tElement.SliceName ?? "",
                        Property = "Cardinality",
                        OldValue = $"{(sElement.Min.HasValue ? sElement.Min.ToString() : "0")}..{sElement.Max ?? "*"}",
                        NewValue = $"{(tElement.Min.HasValue ? tElement.Min.ToString() : "0")}..{tElement.Max ?? "*"}"
                    });
                }
                
                // Fixed/Pattern values
                if (sElement.Fixed != null || tElement.Fixed != null)
                {
                    var sFixed = sElement.Fixed?.ToString() ?? "null";
                    var tFixed = tElement.Fixed?.ToString() ?? "null";
                    
                    var sFixedBase = sFixed.Contains('|') ? sFixed.Split('|')[0] : sFixed;
                    var tFixedBase = tFixed.Contains('|') ? tFixed.Split('|')[0] : tFixed;

                    if (sFixedBase != tFixedBase)
                    {
                        delta.ModifiedElements.Add(new ElementDelta
                        {
                            Path = tElement.Path,
                            SliceName = tElement.SliceName ?? "",
                            Property = "FixedValue",
                            OldValue = ShortenValue(sFixed),
                            NewValue = ShortenValue(tFixed)
                        });
                    }
                }
            }
        }

        // Find removed
        foreach (var sElement in sourceElements)
        {
            if (!targetElements.Any(e => e.ElementId == sElement.ElementId))
            {
                delta.RemovedElements.Add(new ElementDelta { Path = sElement.Path, SliceName = sElement.SliceName ?? "" });
            }
        }

        return delta;
    }

    private List<ProfileDependencyEdge> BuildDependencyEdges(List<StructureDefinition> targetProfiles)
    {
        var edges = new List<ProfileDependencyEdge>();
        foreach (var sd in targetProfiles)
        {
            if (sd.Differential?.Element == null) continue;
            foreach (var el in sd.Differential.Element)
            {
                if (el.Type == null) continue;
                foreach (var t in el.Type)
                {
                    var refs = new List<string>();
                    if (t.Profile != null) refs.AddRange(t.Profile);
                    if (t.TargetProfile != null) refs.AddRange(t.TargetProfile);
                    foreach (var r in refs)
                    {
                        var childUrl = r.Split('|')[0];
                        var pathParts = el.Path?.Split('.') ?? Array.Empty<string>();
                        var fieldName = pathParts.Length > 0 ? pathParts.Last() : "";
                        var label = !string.IsNullOrEmpty(el.SliceName) ? $"{fieldName}:{el.SliceName}" : fieldName;
                        
                        edges.Add(new ProfileDependencyEdge
                        {
                            ParentUrl = sd.Url?.Split('|')[0] ?? string.Empty,
                            ParentName = sd.Name ?? sd.Id ?? string.Empty,
                            ChildUrl = childUrl,
                            ReferenceLabel = label
                        });
                    }
                }
            }
        }
        return edges;
    }

    private List<string> FindProfilePaths(string currentUrl, string currentName, List<ProfileDependencyEdge> edges, HashSet<string> visited)
    {
        if (string.IsNullOrEmpty(currentUrl) || !visited.Add(currentUrl)) return new List<string> { currentName };

        var parents = edges.Where(e => e.ChildUrl == currentUrl).ToList();
        if (!parents.Any()) return new List<string> { currentName };

        var allPaths = new List<string>();
        foreach (var parent in parents)
        {
            var parentPaths = FindProfilePaths(parent.ParentUrl, parent.ParentName, edges, new HashSet<string>(visited));
            foreach (var path in parentPaths)
            {
                allPaths.Add($"{path} ({parent.ReferenceLabel}) -> {currentName}");
            }
        }

        return allPaths.Distinct().ToList();
    }

    private string ShortenValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "null";
        if (value.StartsWith("http") && value.Contains('/')) return value.Split('/').Last();
        return value;
    }

    private List<StructureDefinition> LoadStructureDefinitions(string directory)
    {
        var profiles = new List<StructureDefinition>();
        var parser = new FhirJsonParser();
        
        var files = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            try
            {
                var content = File.ReadAllText(file);
                // Quickly check if it's a StructureDefinition to avoid parsing everything
                if (content.Contains("\"resourceType\":\"StructureDefinition\"") || content.Contains("\"resourceType\": \"StructureDefinition\""))
                {
                    var sd = parser.Parse<StructureDefinition>(content);
                    profiles.Add(sd);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse FHIR resource from {File}", file);
            }
        }
        return profiles;
    }

    private async Task<string> DownloadAndExtractPackageAsync(string packageId, string version)
    {
        var targetDir = Path.Combine(_baseProfileDir, $"{packageId}-{version}");
        if (Directory.Exists(targetDir) && Directory.GetFiles(targetDir, "*.json", SearchOption.AllDirectories).Any())
        {
            return targetDir;
        }

        Directory.CreateDirectory(targetDir);
        var url = $"https://packages.simplifier.net/{packageId}/{version}";
        
        _logger.LogInformation("Downloading package from {Url}", url);
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var gzipStream = new GZipStream(stream, CompressionMode.Decompress);
        using var memoryStream = new MemoryStream();
        await gzipStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        TarFile.ExtractToDirectory(memoryStream, targetDir, overwriteFiles: true);
        
        return targetDir;
    }

    private async Task<string> FetchChangelogAsync(string packageId, string sourceVersion, string targetVersion)
    {
        try
        {
            var url = $"https://packages.simplifier.net/{packageId}";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return string.Empty;

            var jsonStr = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonStr);
            if (doc.RootElement.TryGetProperty("versions", out var versionsProp))
            {
                var cleanSrc = sourceVersion.Split('-')[0];
                var cleanTgt = targetVersion.Split('-')[0];
                
                if (Version.TryParse(cleanSrc, out var srcVer) && Version.TryParse(cleanTgt, out var tgtVer))
                {
                    var validLogs = new Dictionary<Version, string>();
                    foreach (var prop in versionsProp.EnumerateObject())
                    {
                        var cleanV = prop.Name.Split('-')[0];
                        if (Version.TryParse(cleanV, out var v))
                        {
                            if (v > srcVer && v <= tgtVer)
                            {
                                if (prop.Value.TryGetProperty("description", out var descProp))
                                {
                                    var desc = descProp.GetString();
                                    if (!string.IsNullOrWhiteSpace(desc))
                                    {
                                        validLogs[v] = $"### Release {prop.Name}\n{desc}";
                                    }
                                }
                            }
                        }
                    }
                    if (validLogs.Any())
                    {
                        return string.Join("\n\n", validLogs.OrderByDescending(k => k.Key).Select(k => k.Value));
                    }
                }

                // Fallback
                if (versionsProp.TryGetProperty(targetVersion, out var versionProp))
                {
                    if (versionProp.TryGetProperty("description", out var descProp))
                    {
                        return descProp.GetString() ?? string.Empty;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch changelog for {PackageId} {Version}", packageId, targetVersion);
        }
        return string.Empty;
    }
}
