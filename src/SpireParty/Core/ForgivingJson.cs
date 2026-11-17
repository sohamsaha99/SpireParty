using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SpireParty.Core;

/// <summary>
/// Reads settings JSON without ever throwing (hard rule 9): missing, empty or broken input,
/// unknown keys, wrong types and out-of-range values all fall back to the code default,
/// and each fallback is recorded as a warning for the log.
/// </summary>
public sealed class ForgivingJson
{
    readonly JsonElement _root;
    readonly string _path;
    public List<string> Warnings { get; }

    ForgivingJson(JsonElement root, string path, List<string> warnings)
    {
        _root = root;
        _path = path;
        Warnings = warnings;
    }

    public static ForgivingJson Parse(string? json, string fileName)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
            return new ForgivingJson(default, fileName, warnings);
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            var root = doc.RootElement.Clone();
            if (root.ValueKind != JsonValueKind.Object)
            {
                warnings.Add($"{fileName}: top level is not an object; using defaults");
                return new ForgivingJson(default, fileName, warnings);
            }
            return new ForgivingJson(root, fileName, warnings);
        }
        catch (JsonException e)
        {
            warnings.Add($"{fileName}: not valid JSON ({e.Message}); using defaults");
            return new ForgivingJson(default, fileName, warnings);
        }
    }

    bool TryGet(string key, out JsonElement value)
    {
        value = default;
        return _root.ValueKind == JsonValueKind.Object && _root.TryGetProperty(key, out value)
               && value.ValueKind != JsonValueKind.Null;
    }

    public ForgivingJson Section(string key)
    {
        if (TryGet(key, out var v) && v.ValueKind == JsonValueKind.Object)
            return new ForgivingJson(v, $"{_path}.{key}", Warnings);
        if (TryGet(key, out _)) Warnings.Add($"{_path}.{key}: expected an object; using defaults");
        return new ForgivingJson(default, $"{_path}.{key}", Warnings);
    }

    public int Int(string key, int fallback, int min = int.MinValue, int max = int.MaxValue)
    {
        if (!TryGet(key, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) && n >= min && n <= max) return n;
        Warnings.Add($"{_path}.{key}: expected a whole number from {min} to {max}; using {fallback}");
        return fallback;
    }

    public ulong ULong(string key, ulong fallback, ulong min = 0, ulong max = ulong.MaxValue)
    {
        if (!TryGet(key, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetUInt64(out ulong n) && n >= min && n <= max) return n;
        Warnings.Add($"{_path}.{key}: expected a whole number from {min} to {max}; using {fallback}");
        return fallback;
    }

    public double Double(string key, double fallback, double min, double max)
    {
        if (!TryGet(key, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && d >= min && d <= max) return d;
        Warnings.Add($"{_path}.{key}: expected a number from {min} to {max}; using {fallback}");
        return fallback;
    }

    public bool Bool(string key, bool fallback)
    {
        if (!TryGet(key, out var v)) return fallback;
        if (v.ValueKind is JsonValueKind.True or JsonValueKind.False) return v.GetBoolean();
        Warnings.Add($"{_path}.{key}: expected true or false; using {fallback.ToString().ToLowerInvariant()}");
        return fallback;
    }

    public string String(string key, string fallback)
    {
        if (!TryGet(key, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? fallback;
        Warnings.Add($"{_path}.{key}: expected text; using \"{fallback}\"");
        return fallback;
    }

    /// <summary>A string that must be one of <paramref name="allowed"/> (case-insensitive).</summary>
    public string Choice(string key, string fallback, params string[] allowed)
    {
        if (!TryGet(key, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            var match = allowed.FirstOrDefault(a => string.Equals(a, s, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        Warnings.Add($"{_path}.{key}: expected one of {string.Join(", ", allowed)}; using {fallback}");
        return fallback;
    }

    /// <summary>Each element of an array of objects; non-objects are skipped with a warning.</summary>
    public IEnumerable<ForgivingJson> Objects(string key)
    {
        if (!TryGet(key, out var v)) yield break;
        if (v.ValueKind != JsonValueKind.Array)
        {
            Warnings.Add($"{_path}.{key}: expected a list; ignoring it");
            yield break;
        }
        int i = 0;
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
                yield return new ForgivingJson(item, $"{_path}.{key}[{i}]", Warnings);
            else
                Warnings.Add($"{_path}.{key}[{i}]: expected an object; skipping it");
            i++;
        }
    }
}
