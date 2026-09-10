namespace ClipStudio.Application.Parsing;

/// <summary>
/// One node of a parsed Valve KeyValues document: its scalar values and its child blocks.
/// </summary>
/// <remarks>
/// Keys are compared case-insensitively, because Steam is not consistent about their casing
/// between files or between client versions.
/// </remarks>
public sealed class ValveKeyValueNode
{
    /// <summary>Gets the scalar values directly under this node.</summary>
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the child blocks directly under this node.</summary>
    public Dictionary<string, ValveKeyValueNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns a scalar value, or null when the key is absent.</summary>
    /// <param name="key">The key to read.</param>
    /// <returns>The value, or null.</returns>
    public string? Value(string key) => Values.GetValueOrDefault(key);

    /// <summary>Returns a child block, or null when the key is absent.</summary>
    /// <param name="key">The key to read.</param>
    /// <returns>The child node, or null.</returns>
    public ValveKeyValueNode? Child(string key) => Children.GetValueOrDefault(key);
}
