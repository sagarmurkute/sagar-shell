using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Threading.Tasks;
using SagarShell.Engine.Execution;
using SagarShell.Engine.Intelligence;
using SagarShell.Engine.Models;
using SagarShell.Engine.Pipeline;
using Xunit;

namespace SagarShell.Engine.Tests;

public class StructuredObjectPipelineTests
{
    private readonly NaturalLanguageEngine _engine = new();

    private List<PSObject> CreateSampleTestObjects()
    {
        var list = new List<PSObject>();

        var p1 = new PSObject();
        p1.Properties.Add(new PSNoteProperty("Name", "explorer"));
        p1.Properties.Add(new PSNoteProperty("Id", 1200));
        p1.Properties.Add(new PSNoteProperty("MemoryMB", 150.5));
        list.Add(p1);

        var p2 = new PSObject();
        p2.Properties.Add(new PSNoteProperty("Name", "chrome"));
        p2.Properties.Add(new PSNoteProperty("Id", 4500));
        p2.Properties.Add(new PSNoteProperty("MemoryMB", 512.0));
        list.Add(p2);

        var p3 = new PSObject();
        p3.Properties.Add(new PSNoteProperty("Name", "code"));
        p3.Properties.Add(new PSNoteProperty("Id", 890));
        p3.Properties.Add(new PSNoteProperty("MemoryMB", 320.75));
        list.Add(p3);

        return list;
    }

    [Fact]
    public void Pipeline_ShouldFilterObjectsInMemory()
    {
        var sample = CreateSampleTestObjects();

        // Filter: MemoryMB > 200
        var filtered = StructuredObjectPipeline.Filter(sample, "MemoryMB", ">", "200");
        Assert.Equal(2, filtered.Count);
        Assert.Contains(filtered, o => o.Properties["Name"].Value.ToString() == "chrome");
        Assert.Contains(filtered, o => o.Properties["Name"].Value.ToString() == "code");

        // Filter: Name equals "explorer"
        var single = StructuredObjectPipeline.Filter(sample, "Name", "=", "explorer");
        Assert.Single(single);
        Assert.Equal(1200, Convert.ToInt32(single[0].Properties["Id"].Value));
    }

    [Fact]
    public void Pipeline_ShouldSortObjectsInMemory()
    {
        var sample = CreateSampleTestObjects();

        // Sort by Id Ascending
        var sortedAsc = StructuredObjectPipeline.Sort(sample, "Id", descending: false);
        Assert.Equal("code", sortedAsc[0].Properties["Name"].Value.ToString()); // Id 890
        Assert.Equal("explorer", sortedAsc[1].Properties["Name"].Value.ToString()); // Id 1200
        Assert.Equal("chrome", sortedAsc[2].Properties["Name"].Value.ToString()); // Id 4500

        // Sort by MemoryMB Descending
        var sortedDesc = StructuredObjectPipeline.Sort(sample, "MemoryMB", descending: true);
        Assert.Equal("chrome", sortedDesc[0].Properties["Name"].Value.ToString()); // 512MB
        Assert.Equal("code", sortedDesc[1].Properties["Name"].Value.ToString()); // 320.75MB
    }

    [Fact]
    public void Pipeline_ShouldSelectPropertiesInMemory()
    {
        var sample = CreateSampleTestObjects();

        var projected = StructuredObjectPipeline.SelectProperties(sample, new[] { "Name", "MemoryMB" });
        Assert.Equal(3, projected.Count);
        Assert.True(projected[0].ContainsKey("Name"));
        Assert.True(projected[0].ContainsKey("MemoryMB"));
        Assert.False(projected[0].ContainsKey("Id"));
    }

    [Fact]
    public void Pipeline_ShouldInspectObjectStructure()
    {
        var sample = CreateSampleTestObjects();
        var report = StructuredObjectPipeline.Inspect(sample[0]);

        Assert.NotNull(report);
        Assert.Equal(3, report.Properties.Count);
        Assert.Contains(report.Properties, p => p.Name == "Name" && p.Value == "explorer");
        Assert.Contains(report.Properties, p => p.Name == "Id" && p.Value == "1200");

        var text = report.ToString();
        Assert.Contains("explorer", text);
        Assert.Contains("MemoryMB", text);
    }

    [Fact]
    public void Pipeline_ShouldExportToJsonAndCsvInMemory()
    {
        var sample = CreateSampleTestObjects();

        // JSON Export
        var json = StructuredObjectPipeline.ExportToJson(sample, indented: true);
        Assert.NotNull(json);
        Assert.Contains("\"Name\": \"explorer\"", json);
        Assert.Contains("\"MemoryMB\": 150.5", json);

        // CSV Export
        var csv = StructuredObjectPipeline.ExportToCsv(sample);
        Assert.NotNull(csv);
        Assert.Contains("Name,Id,MemoryMB", csv);
        Assert.Contains("explorer,1200,150.5", csv);
        Assert.Contains("chrome,4500,512", csv);
    }

    [Fact]
    public async Task EndToEnd_ObjectPipeline_CachingAndFollowUpExports()
    {
        using var executor = new InProcessRunspaceExecutor();
        var context = new ShellContext(Directory.GetCurrentDirectory());

        // Step 1: Execute query producing structured PSObjects
        var req1 = new TranslationRequest("show running processes", context);
        var trans1 = _engine.Translate(req1);
        var exec1 = await executor.ExecuteAsync(trans1, context);

        Assert.True(exec1.Success);
        Assert.NotEmpty(context.LastOutputObjects);
        Assert.True(context.LastOutputObjects.Count > 0);

        // Step 2: Follow-up in natural language: "export to json"
        var req2 = new TranslationRequest("export to json", context);
        var trans2 = _engine.Translate(req2);
        var exec2 = await executor.ExecuteAsync(trans2, context);

        Assert.True(exec2.Success);
        Assert.StartsWith("[", exec2.StandardOutput.Trim());
        Assert.Contains("ProcessName", exec2.StandardOutput);

        // Step 3: Follow-up in natural language: "inspect the first one"
        var req3 = new TranslationRequest("inspect the first one", context);
        var trans3 = _engine.Translate(req3);
        var exec3 = await executor.ExecuteAsync(trans3, context);

        Assert.True(exec3.Success);
        Assert.Contains("Property", exec3.StandardOutput);
        Assert.Contains("Value", exec3.StandardOutput);
    }
}
