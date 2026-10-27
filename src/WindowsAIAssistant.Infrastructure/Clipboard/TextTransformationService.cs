using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Models.Clipboard;

namespace WindowsAIAssistant.Infrastructure.Clipboard;

public sealed class TextTransformationService : ITextTransformationService
{
    public Task<string> FormatJsonAsync(string json, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(json))
        {
            return Task.FromResult(json ?? string.Empty);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var options = new JsonSerializerOptions { WriteIndented = true };
            var formatted = System.Text.Json.JsonSerializer.Serialize(doc.RootElement, options);
            return Task.FromResult(formatted);
        }
        catch
        {
            return Task.FromResult(json);
        }
    }

    public Task<string> FormatXmlAsync(string xml, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return Task.FromResult(xml ?? string.Empty);
        }
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            return Task.FromResult(doc.ToString());
        }
        catch
        {
            return Task.FromResult(xml);
        }
    }

    public Task<string> FormatSqlAsync(string sql, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(sql))
        {
            return Task.FromResult(sql ?? string.Empty);
        }
        // Basic formatting - keep simple
        var formatted = sql.Replace("SELECT ", "\nSELECT ", StringComparison.OrdinalIgnoreCase)
            .Replace("FROM ", "\nFROM ", StringComparison.OrdinalIgnoreCase)
            .Replace("WHERE ", "\nWHERE ", StringComparison.OrdinalIgnoreCase)
            .Replace("JOIN ", "\nJOIN ", StringComparison.OrdinalIgnoreCase)
            .Replace("LEFT JOIN ", "\nLEFT JOIN ", StringComparison.OrdinalIgnoreCase)
            .Replace("ORDER BY ", "\nORDER BY ", StringComparison.OrdinalIgnoreCase)
            .Replace("GROUP BY ", "\nGROUP BY ", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(formatted.Trim());
    }

    public Task<string> FormatMarkdownAsync(string markdown, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return Task.FromResult(markdown ?? string.Empty);
        }
        return Task.FromResult(markdown.TrimEnd() + Environment.NewLine);
    }

    public bool IsValidJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }
        try
        {
            JsonDocument.Parse(json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task<string> ConvertJsonToTypeScriptAsync(string json, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(json))
        {
            return Task.FromResult(json ?? string.Empty);
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("interface Root {");
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var tsType = GetTypeScriptType(prop.Value);
                    sb.AppendLine($"  {prop.Name}: {tsType};");
                }
            }
            sb.AppendLine("}");
            return Task.FromResult(sb.ToString());
        }
        catch
        {
            return Task.FromResult(json);
        }
    }

    public Task<string> ConvertJsonToCSharpAsync(string json, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(json))
        {
            return Task.FromResult(json ?? string.Empty);
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("public class Root");
            sb.AppendLine("{");
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var csType = GetCSharpType(prop.Value);
                    var name = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(prop.Name);
                    sb.AppendLine($"    public {csType} {name} {{ get; set; }}");
                }
            }
            sb.AppendLine("}");
            return Task.FromResult(sb.ToString());
        }
        catch
        {
            return Task.FromResult(json);
        }
    }

    private static string GetTypeScriptType(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Null => "null",
            JsonValueKind.Array => "any[]",
            JsonValueKind.Object => "any",
            _ => "any"
        };
    }

    private static string GetCSharpType(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "decimal",
            JsonValueKind.True or JsonValueKind.False => "bool",
            JsonValueKind.Null => "string?",
            JsonValueKind.Array => "object[]",
            JsonValueKind.Object => "object",
            _ => "object"
        };
    }
}
