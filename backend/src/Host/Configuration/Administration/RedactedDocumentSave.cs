// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Configuration;

namespace MailFathom.Host.Configuration.Administration;

/// <summary>Turns a document somebody edited back into the changes that carry the stored one to it.</summary>
/// <remarks>
/// <para>
/// Two documents in this system are handed out redacted and taken back edited — the deployment's own persisted
/// configuration, and one owner's record — and both are JSON objects of configuration keys carrying secret references
/// among their settings. The rules for reading a save are therefore one set of rules, stated here once: what a saved
/// buffer changes, and which redaction markers a save is allowed to leave standing.
/// </para>
/// <para>
/// The second is the part that is not obvious. A marker stands for whatever the document held at the same
/// configuration path, and that is sound only while the path means the same thing in both documents. An object
/// property name does mean the same thing — nothing renumbers an object — so a marker beneath one is placeable
/// whenever the document carried the path at all. An array position does not: deleting the first of two mail accounts
/// moves the second to index <c>0</c>, and a marker standing at that index would be dropped as unchanged while every
/// other key at index <c>0</c> became the surviving element's — committing that element with the deleted one's
/// credential. The candidate binds, every validator passes, and the path is in none of the changes a commit reports,
/// so nothing else in this system would notice.
/// </para>
/// <para>
/// So a marker whose path passes through an array position is placeable only where the whole element it sits in is
/// what the buffer was opened over, compared against the buffer rather than against the row, since the buffer is what
/// the person was shown. The refusal that follows costs them the ability to change a neighbouring setting of a
/// secret-bearing element inside an editing session, and names the narrower change that does it without touching the
/// reference. That is the trade this makes deliberately: a save nobody can place is refused rather than committed on
/// the strength of a position.
/// </para>
/// </remarks>
internal static class RedactedDocumentSave
{
    /// <summary>Reads a document as the configuration keys the deployment would compose from it.</summary>
    /// <param name="json">The document.</param>
    /// <returns>Every setting the document supplies, keyed the way every provider in the pipeline keys one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidDataException">Thrown when the JSON configuration provider refuses the document.</exception>
    /// <remarks>
    /// Flattened by the framework's own JSON configuration provider — the same parser that reads the row at startup —
    /// so a nested object, an array, and a number become exactly the keys the deployment would have read, rather than
    /// the keys a second flattener here happened to agree on.
    /// </remarks>
    internal static Dictionary<string, string> Flatten(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var buffer = new MemoryStream(Encoding.UTF8.GetBytes(json));

        // Released with the stream. The root owns the provider it built and that provider's reload-token registration,
        // and a save flattens both documents; RootSettingsWriter.Judge disposes its own composition for the same
        // reason.
        using var composed = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(buffer).Build();

        return composed
            .AsEnumerable()
            .Where(setting => setting.Value is not null)
            .ToDictionary(setting => setting.Key, setting => setting.Value!, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Names every redaction marker the saved buffer leaves standing that this deployment cannot place.</summary>
    /// <param name="standing">The document the buffer was opened over, with its references as the row holds them.</param>
    /// <param name="saved">The document the person saved, with a marker wherever a reference stood.</param>
    /// <param name="remedy">The clause naming what the reader does instead, which is the caller's because what a surface has to offer differs: a document with a single-setting change names it, and one without names the act that states the secret afresh.</param>
    /// <returns>One sentence per marker the save cannot account for, empty where every one of them is placeable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="standing" /> or <paramref name="saved" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="remedy" /> is <see langword="null" />, empty, or white space.</exception>
    internal static IReadOnlyList<string> FindMarkersTheSaveCannotPlace(
        Dictionary<string, string> standing,
        Dictionary<string, string> saved,
        string remedy)
    {
        ArgumentNullException.ThrowIfNull(standing);
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentException.ThrowIfNullOrWhiteSpace(remedy);

        return
        [
            .. saved
                .Where(setting => string.Equals(setting.Value, SettingRedaction.Marker, StringComparison.Ordinal))
                .Select(setting => WhyItCannotBePlaced(standing, saved, setting.Key, remedy))
                .OfType<string>()
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>Turns the difference between two documents into the changes that carry one to the other.</summary>
    /// <param name="standing">The document as it stands.</param>
    /// <param name="saved">The document the person saved.</param>
    /// <returns>The changes, ordered by path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// A value left at the redaction marker is not a change, and dropping it is what makes an editing session safe over
    /// a document carrying secrets: the buffer shows the marker where a reference stands, and saving it back leaves the
    /// reference exactly as it was rather than persisting the marker over it. A setting the person deleted is a removal
    /// like any other, marker or not.
    /// </remarks>
    internal static IReadOnlyList<ConfigurationEdit> DifferenceBetween(
        Dictionary<string, string> standing,
        Dictionary<string, string> saved)
    {
        ArgumentNullException.ThrowIfNull(standing);
        ArgumentNullException.ThrowIfNull(saved);

        return
        [
            .. saved
                .Where(setting => !string.Equals(setting.Value, SettingRedaction.Marker, StringComparison.Ordinal))
                .Where(setting => !standing.TryGetValue(setting.Key, out var held)
                    || !string.Equals(held, setting.Value, StringComparison.Ordinal))
                .Select(setting => ConfigurationEdit.SetTo(setting.Key, setting.Value))
                .Concat(standing
                    .Where(setting => !saved.ContainsKey(setting.Key))
                    .Select(setting => ConfigurationEdit.Removing(setting.Key)))
                .OrderBy(edit => edit.Path, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>Says why one marker cannot be placed, or nothing where it can.</summary>
    private static string? WhyItCannotBePlaced(
        Dictionary<string, string> standing,
        Dictionary<string, string> saved,
        string path,
        string remedy)
    {
        if (!standing.ContainsKey(path))
        {
            return $"{path} was saved as the redaction marker, and the document carried no setting there for it to stand for. Write the value the setting takes, or leave the setting out of the document.";
        }

        if (FirstIndexedAncestorOf(path) is not { } element)
        {
            return null;
        }

        return SubtreeUnchanged(standing, saved, element)
            ? null
            : $"{path} was saved as the redaction marker while '{element}' changed, so this deployment cannot tell which secret the marker stands for — a position moves when an element is added or removed. Save that element as it was opened, or {remedy}";
    }

    /// <summary>Reports whether everything beneath a path is what the buffer was opened over.</summary>
    /// <remarks>
    /// Compared against the redacted reading rather than against the row, because that is what the person was handed:
    /// a value the buffer showed as the marker is unchanged exactly when the save still shows the marker there.
    /// </remarks>
    private static bool SubtreeUnchanged(
        Dictionary<string, string> standing,
        Dictionary<string, string> saved,
        string element)
    {
        var opened = Beneath(standing, element)
            .ToDictionary(
                setting => setting.Key,
                setting => SettingRedaction.Apply(setting.Key, setting.Value),
                StringComparer.OrdinalIgnoreCase);

        var written = Beneath(saved, element).ToArray();

        return written.Length == opened.Count
            && written.All(setting => opened.TryGetValue(setting.Key, out var held)
                && string.Equals(held, setting.Value, StringComparison.Ordinal));
    }

    /// <summary>Names the settings a document carries at or beneath a path.</summary>
    private static IEnumerable<KeyValuePair<string, string>> Beneath(
        Dictionary<string, string> document,
        string prefix) =>
        document.Where(setting => setting.Key.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || setting.Key.StartsWith($"{prefix}:", StringComparison.OrdinalIgnoreCase));

    /// <summary>Names the element a path sits in, up to and including its outermost array position, or nothing where it passes through none.</summary>
    /// <remarks>
    /// The outermost rather than the innermost, because a path may pass through several and every one of them can move:
    /// a rule at <c>Mail:Accounts:0:Rules:2</c> is re-paired by a change to the accounts as readily as by a change to
    /// the rules, and the account is what contains both.
    /// </remarks>
    private static string? FirstIndexedAncestorOf(string path)
    {
        var segments = path.Split(':');
        var position = Array.FindIndex(
            segments,
            segment => segment.Length > 0 && segment.All(char.IsAsciiDigit));

        return position < 0 ? null : string.Join(':', segments[..(position + 1)]);
    }
}
