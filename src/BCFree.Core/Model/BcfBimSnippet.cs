namespace BCFree.Core.Model;

public sealed record BcfBimSnippet(
    string SnippetType,
    string? Reference = null,
    string? ReferenceSchema = null,
    bool IsExternal = false);
