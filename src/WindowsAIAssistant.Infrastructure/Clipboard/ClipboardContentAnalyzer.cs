using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WindowsAIAssistant.Core.Abstractions.Clipboard;
using WindowsAIAssistant.Core.Models.Clipboard;

namespace WindowsAIAssistant.Infrastructure.Clipboard;

public sealed class ClipboardContentAnalyzer : IClipboardContentAnalyzer
{
    public Task<ClipboardContent> AnalyzeAsync(ClipboardContent content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (content == null)
        {
            content = new ClipboardContent();
        }

        content.Type = DetectContentType(content);
        return Task.FromResult(content);
    }

    public ClipboardContentType DetectContentType(ClipboardContent content)
    {
        if (content == null)
        {
            return ClipboardContentType.Unknown;
        }

        if (content.HasImage)
        {
            return ClipboardContentType.Image;
        }

        if (content.HasFiles && content.FilePaths.Count > 0)
        {
            return ClipboardContentType.File;
        }

        if (string.IsNullOrWhiteSpace(content.Text))
        {
            return ClipboardContentType.Unknown;
        }

        var text = content.Text.Trim();

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return ClipboardContentType.Url;
        }

        if (text.StartsWith("<") && text.EndsWith(">"))
        {
            if (text.Contains("<html", StringComparison.OrdinalIgnoreCase))
            {
                return ClipboardContentType.Html;
            }
            if (text.Contains("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase))
            {
                return ClipboardContentType.Html;
            }
            // try xml
            if (text.Contains("<?xml", StringComparison.OrdinalIgnoreCase) || text.Count(c => c == '<') > 1)
            {
                return ClipboardContentType.Xml;
            }
        }

        if ((text.StartsWith("{") && text.EndsWith("}")) || (text.StartsWith("[") && text.EndsWith("]")))
        {
            try
            {
                JsonDocument.Parse(text);
                return ClipboardContentType.Json;
            }
            catch
            {
                // might still be json-like
            }
        }

        if (text.Contains("SELECT ", StringComparison.OrdinalIgnoreCase) || 
            text.Contains("INSERT ", StringComparison.OrdinalIgnoreCase) || 
            text.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase) || 
            text.Contains("DELETE ", StringComparison.OrdinalIgnoreCase) || 
            text.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase))
        {
            return ClipboardContentType.Sql;
        }

        if (text.Contains("```") || 
            text.Contains("using ", StringComparison.Ordinal) || 
            text.Contains("class ", StringComparison.Ordinal) || 
            text.Contains("function ", StringComparison.Ordinal) || 
            text.Contains("def ", StringComparison.Ordinal) || 
            text.Contains("public static", StringComparison.Ordinal) ||
            text.Contains("import ", StringComparison.Ordinal))
        {
            return ClipboardContentType.Code;
        }

        if (text.Contains("\t") || text.Contains(",") && text.Split('\n').Length > 1 && text.Split(',').Length > text.Split('\n').Length)
        {
            // heuristic for csv
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 1)
            {
                var firstCommaCount = lines[0].Split(',').Length;
                var secondCommaCount = lines[1].Split(',').Length;
                if (firstCommaCount == secondCommaCount && firstCommaCount > 1)
                {
                    return ClipboardContentType.Csv;
                }
            }
        }

        if (text.Contains("# ") || text.Contains("## ") || text.Contains("**"))
        {
            return ClipboardContentType.Markdown;
        }

        if (text.Contains("@") && text.Contains(".") && !text.Contains(" ") && text.Length < 200)
        {
            return ClipboardContentType.Email;
        }

        return ClipboardContentType.PlainText;
    }

    public double GetConfidence(ClipboardContent content, ClipboardContentType type)
    {
        if (type == ClipboardContentType.Json && !string.IsNullOrWhiteSpace(content.Text))
        {
            try { JsonDocument.Parse(content.Text.Trim()); return 1.0; } catch { }
        }
        if (type == ClipboardContentType.Url && !string.IsNullOrWhiteSpace(content.Text) && Uri.TryCreate(content.Text.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)) return 1.0;
        if (type == ClipboardContentType.Image && content.HasImage) return 1.0;
        if (type == ClipboardContentType.File && content.HasFiles) return 1.0;
        return 0.7;
    }
}
