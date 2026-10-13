using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ACA360.Core.Models;

// Stable response contracts for the server-paged Flag Fix Grid.
public sealed class FlaggedGridDataResult
{
    public List<FlaggedEmployeeGrid> Employees { get; set; } = new();
    public List<FlagGridDefinition> FlagDefs { get; set; } = new();
    public int TotalRecords { get; set; }
    public int FilteredRecords { get; set; }
}
public sealed class FlaggedGridCounts
{
    public int TotalRecords { get; set; }
    public int FilteredRecords { get; set; }
}
public class FlagGridDefinition
{
    public int FlagId { get; set; }
    public string? FlagCode { get; set; }
    public string? Description { get; set; }
    public string? Severity { get; set; }
    public string? TargetField { get; set; }
    public string? TargetTable { get; set; }
    public int? UsageCount { get; set; }
    // Current assignment rows (including separate months), not historical engine runs.
    public long? OccurrenceCount { get; set; }
}
