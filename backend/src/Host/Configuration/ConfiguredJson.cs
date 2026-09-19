// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;

namespace MailFathom.Host.Configuration;

/// <summary>Reads a configuration subtree back as the JSON an operator wrote into it.</summary>
/// <remarks>
/// <para>
/// Configuration is a flat map of string keys to string values whatever provider filled it, so a setting whose value is
/// a whole JSON document — a request member a provider documents as an object, an array of them — arrives here as a
/// tree of leaves and has to be reassembled. The options binder cannot do it: a property it could bind such a tree to
/// would have to be a recursive union of object, array, and scalar, which is what <see cref="JsonElement" /> already
/// is and what no binder target can be. So the subtree is bound as itself and read here instead.
/// </para>
/// <para>
/// Two things configuration cannot state are therefore decided here rather than read. Whether a node is an object or
/// an array, because both arrive as children keyed by name and an array's names are its indices: that is read back the
/// way the binder reads a collection, so a node whose children are exactly the integers below their own count is an
/// array in index order and anything else is an object, and the ambiguity left over is an object whose own members are
/// named <c>0</c> and <c>1</c>, which arrives as a two-element array. And whether an empty structure was written at
/// all, because an object with no members carries neither a value nor a child and neither does a null, so it arrives
/// as null. Nothing can tell either pair apart, and the reference states both rather than leaving a reader to find out.
/// </para>
/// </remarks>
internal static class ConfiguredJson
{
    /// <summary>The deepest a declared subtree may nest, counting the member itself as the first level.</summary>
    /// <remarks>
    /// Generous against every request member a provider documents — a routing block whose price ceiling is an object of
    /// its own reaches three — and present because the reader below descends a tree an operator wrote. A configuration
    /// key is bounded by nothing, so a bound here is what keeps the descent bounded, and <see cref="DepthOf" /> stops
    /// measuring one level past it rather than walking whatever was declared.
    /// </remarks>
    public const int GreatestNestingDepth = 16;

    /// <summary>Reads each immediate child of a section as the JSON it states.</summary>
    /// <param name="section">The section, or <see langword="null" /> where the deployment declared none.</param>
    /// <returns>One entry per child, keyed by the name it was written under.</returns>
    public static Dictionary<string, JsonElement> ReadMembers(IConfigurationSection? section) =>
        section is null
            ? []
            : section.GetChildren().ToDictionary(child => child.Key, Read, StringComparer.Ordinal);

    /// <summary>Reads one node as the JSON it states.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The value, which is an object or an array where the node has children and a scalar where it does not.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="node" /> is <see langword="null" />.</exception>
    /// <remarks>Call it only for a node <see cref="DepthOf" /> has already proved shallow enough, since the descent is as deep as the tree.</remarks>
    public static JsonElement Read(IConfigurationSection node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var children = node.GetChildren().ToArray();

        if (children.Length == 0)
        {
            return ReadScalar(node.Value);
        }

        return IsArray(children)
            ? JsonSerializer.SerializeToElement(children.OrderBy(IndexOf).Select(Read).ToArray())
            : JsonSerializer.SerializeToElement(children.ToDictionary(child => child.Key, Read, StringComparer.Ordinal));
    }

    /// <summary>Measures how deeply a node nests, stopping one level past <see cref="GreatestNestingDepth" />.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The number of levels, which is one for a node with no children.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="node" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A level at a time rather than a recursive descent, because this is the check that decides whether descending is
    /// safe at all: measuring a tree by recursing into it would be the very thing the bound exists to prevent.
    /// </remarks>
    public static int DepthOf(IConfigurationSection node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var depth = 0;
        IReadOnlyList<IConfigurationSection> level = [node];

        while (level.Count > 0 && depth <= GreatestNestingDepth)
        {
            depth++;
            level = [.. level.SelectMany(child => child.GetChildren())];
        }

        return depth;
    }

    /// <summary>Reads the type of a leaf back out of the text configuration carried it as.</summary>
    /// <remarks>
    /// A number, a boolean, <c>null</c>, or a whole JSON document written as text all go out as what they state, and
    /// anything else as a string. Writing a member as its JSON text stays supported beside the nested form above,
    /// because a deployment configured through environment variables writes one variable rather than a tree — and it
    /// is also the only way to send the string <c>"40"</c>, which is written <c>"\"40\""</c>.
    /// </remarks>
    private static JsonElement ReadScalar(string? value)
    {
        if (value is null)
        {
            return JsonSerializer.SerializeToElement<string?>(null);
        }

        // The JSON configuration provider hands a boolean over as .NET writes one, capitalized, which JSON does not read.
        if (bool.TryParse(value, out var flag))
        {
            return JsonSerializer.SerializeToElement(flag);
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(value);
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(value);
        }
    }

    /// <summary>Reports whether a node's children are an array's indices rather than an object's member names.</summary>
    private static bool IsArray(IConfigurationSection[] children)
    {
        var taken = new bool[children.Length];

        foreach (var child in children)
        {
            var index = IndexOf(child);

            if (index < 0 || index >= children.Length || taken[index])
            {
                return false;
            }

            taken[index] = true;
        }

        return true;
    }

    /// <summary>Reads a child's key as an array index, or reports that it is not one.</summary>
    /// <remarks><see cref="NumberStyles.None" /> refuses a sign, a separator, and surrounding space, so only the digits a configuration provider writes for a collection are read as a position.</remarks>
    private static int IndexOf(IConfigurationSection child) =>
        int.TryParse(child.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : -1;
}
