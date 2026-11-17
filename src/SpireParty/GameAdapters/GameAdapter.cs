using System;
using System.Collections.Generic;
using HarmonyLib;

namespace SpireParty.GameAdapters;

/// <summary>
/// One Harmony patch site (hard rule 4). <see cref="SelfCheck"/> verifies that every target
/// type and member still exists in the running game build; <see cref="Apply"/> patches.
/// </summary>
public abstract class GameAdapter
{
    /// <summary>Short name shown in logs and the disabled-features message.</summary>
    public abstract string Name { get; }

    /// <summary>SpireParty features that stop working if this adapter cannot be applied.</summary>
    public abstract IReadOnlyList<string> Features { get; }

    /// <summary>Returns null when all targets exist, otherwise a readable reason.</summary>
    public abstract string? SelfCheck();

    public abstract void Apply(Harmony harmony);

    /// <summary>Helper for self-checks: finds a method or reports which one is missing.</summary>
    protected static string? Require(Type? type, string typeName, string? method = null, Type[]? args = null)
    {
        if (type == null) return $"type {typeName} not found";
        if (method == null) return null;
        var found = args == null ? AccessTools.Method(type, method) : AccessTools.Method(type, method, args);
        return found == null ? $"method {typeName}.{method} not found" : null;
    }
}
