using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Text.Json;

namespace SagarShell.Engine.Pipeline;

/// <summary>
/// Provides in-memory filtering, sorting, column selection, property inspection,
/// and exporting (JSON/CSV) for native PowerShell PSObject pipelines without re-querying the OS.
/// </summary>
public static class StructuredObjectPipeline
{
    /// <summary>
    /// Filters PSObjects in-memory by property condition.
    /// </summary>
    public static List<PSObject> Filter(IEnumerable<PSObject> source, string propertyName, string comparisonOp, string targetValue)
    {
        if (source == null) return new List<PSObject>();

        return source.Where(obj =>
        {
            var prop = obj.Properties[propertyName];
            if (prop == null) return false;

            var val = prop.Value;
            if (val == null) return targetValue.Equals("null", StringComparison.OrdinalIgnoreCase);

            var valStr = val.ToString() ?? string.Empty;

            // Numeric comparison if applicable
            if (double.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var numVal) &&
                double.TryParse(targetValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var targetNum))
            {
                return comparisonOp switch
                {
                    ">" or "-gt" or "greater" => numVal > targetNum,
                    ">=" or "-ge" => numVal >= targetNum,
                    "<" or "-lt" or "less" => numVal < targetNum,
                    "<=" or "-le" => numVal <= targetNum,
                    "=" or "==" or "-eq" or "equals" => Math.Abs(numVal - targetNum) < 0.0001,
                    "!=" or "-ne" => Math.Abs(numVal - targetNum) >= 0.0001,
                    _ => false
                };
            }

            // String comparison
            return comparisonOp switch
            {
                "=" or "==" or "-eq" or "equals" or "is" => string.Equals(valStr, targetValue, StringComparison.OrdinalIgnoreCase),
                "!=" or "-ne" or "isnot" => !string.Equals(valStr, targetValue, StringComparison.OrdinalIgnoreCase),
                "contains" or "-like" => valStr.Contains(targetValue, StringComparison.OrdinalIgnoreCase),
                "startswith" => valStr.StartsWith(targetValue, StringComparison.OrdinalIgnoreCase),
                "endswith" => valStr.EndsWith(targetValue, StringComparison.OrdinalIgnoreCase),
                _ => string.Equals(valStr, targetValue, StringComparison.OrdinalIgnoreCase)
            };
        }).ToList();
    }

    /// <summary>
    /// Sorts PSObjects in-memory by property.
    /// </summary>
    public static List<PSObject> Sort(IEnumerable<PSObject> source, string propertyName, bool descending = false)
    {
        if (source == null) return new List<PSObject>();

        return descending
            ? source.OrderByDescending(obj => GetPropertyValueForSort(obj, propertyName)).ToList()
            : source.OrderBy(obj => GetPropertyValueForSort(obj, propertyName)).ToList();
    }

    private static object GetPropertyValueForSort(PSObject obj, string propertyName)
    {
        var prop = obj.Properties[propertyName];
        if (prop == null || prop.Value == null) return string.Empty;

        var val = prop.Value;
        if (val is IComparable) return val;

        if (double.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
            return num;

        return val.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Projects a subset of property names from each PSObject into dynamic dictionary records.
    /// </summary>
    public static List<Dictionary<string, object?>> SelectProperties(IEnumerable<PSObject> source, IEnumerable<string> propertyNames)
    {
        if (source == null) return new List<Dictionary<string, object?>>();

        var propsList = propertyNames.ToList();
        var result = new List<Dictionary<string, object?>>();

        foreach (var item in source)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var propName in propsList)
            {
                var p = item.Properties[propName];
                dict[propName] = p?.Value;
            }
            result.Add(dict);
        }

        return result;
    }

    /// <summary>
    /// Generates a detailed inspection report of a PSObject's properties, types, and values.
    /// </summary>
    public static ObjectInspectionReport Inspect(PSObject obj)
    {
        var typeNames = obj.TypeNames.ToList();
        var mainType = typeNames.FirstOrDefault() ?? obj.BaseObject?.GetType().FullName ?? "PSCustomObject";

        var properties = new List<PropertyInspection>();
        foreach (var prop in obj.Properties)
        {
            try
            {
                properties.Add(new PropertyInspection
                {
                    Name = prop.Name,
                    TypeName = prop.TypeNameOfValue ?? prop.Value?.GetType().Name ?? "Object",
                    Value = prop.Value?.ToString() ?? "null",
                    IsGettable = prop.IsGettable,
                    IsSettable = prop.IsSettable
                });
            }
            catch (Exception ex)
            {
                properties.Add(new PropertyInspection
                {
                    Name = prop.Name,
                    TypeName = "Unknown",
                    Value = $"<Error reading property: {ex.Message}>",
                    IsGettable = false,
                    IsSettable = false
                });
            }
        }

        return new ObjectInspectionReport
        {
            TypeName = mainType,
            AllTypeNames = typeNames,
            Properties = properties
        };
    }

    /// <summary>
    /// Serializes PSObjects to formatted JSON string without invoking external scripts.
    /// </summary>
    public static string ExportToJson(IEnumerable<PSObject> source, bool indented = true)
    {
        if (source == null) return "[]";

        var items = new List<Dictionary<string, object?>>();
        foreach (var obj in source)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var prop in obj.Properties)
            {
                try
                {
                    if (prop.IsGettable && prop.Value != null)
                    {
                        var val = prop.Value;
                        if (val is string || val is int || val is long || val is double || val is bool || val is DateTime)
                        {
                            dict[prop.Name] = val;
                        }
                        else
                        {
                            dict[prop.Name] = val.ToString();
                        }
                    }
                    else
                    {
                        dict[prop.Name] = null;
                    }
                }
                catch
                {
                    // Ignore property access issues during JSON serialization
                }
            }
            items.Add(dict);
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = indented
        };

        return JsonSerializer.Serialize(items, options);
    }

    /// <summary>
    /// Exports PSObjects to standard CSV string.
    /// </summary>
    public static string ExportToCsv(IEnumerable<PSObject> source, string delimiter = ",")
    {
        if (source == null || !source.Any()) return string.Empty;

        var list = source.ToList();
        var headers = list.SelectMany(o => o.Properties.Select(p => p.Name)).Distinct().ToList();

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(delimiter, headers.Select(EscapeCsvField)));

        foreach (var item in list)
        {
            var rowValues = headers.Select(h =>
            {
                var p = item.Properties[h];
                return EscapeCsvField(p?.Value?.ToString() ?? string.Empty);
            });
            sb.AppendLine(string.Join(delimiter, rowValues));
        }

        return sb.ToString();
    }

    private static string EscapeCsvField(string field)
    {
        if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}

public class ObjectInspectionReport
{
    public required string TypeName { get; set; }
    public List<string> AllTypeNames { get; set; } = new();
    public List<PropertyInspection> Properties { get; set; } = new();

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[Type: {TypeName}]");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine($"{"Property",-25} {"Type",-15} {"Value"}");
        sb.AppendLine(new string('-', 60));

        foreach (var p in Properties)
        {
            var valPreview = p.Value.Length > 40 ? p.Value[..37] + "..." : p.Value;
            sb.AppendLine($"{p.Name,-25} {p.TypeName,-15} {valPreview}");
        }

        return sb.ToString().TrimEnd();
    }
}

public class PropertyInspection
{
    public required string Name { get; set; }
    public required string TypeName { get; set; }
    public required string Value { get; set; }
    public bool IsGettable { get; set; }
    public bool IsSettable { get; set; }
}
