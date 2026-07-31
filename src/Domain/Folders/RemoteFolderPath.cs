// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailFathom.Domain.Folders;

/// <summary>Locates a folder the way the mail server advertises it.</summary>
/// <remarks>
/// The path is owned by the server and may change under a stable <see cref="MailFolderAlias" />. It is treated as
/// sensitive metadata, because a folder path can itself carry personal or organizational information, and it is
/// therefore written only to the audit event that records a mapping change.
/// </remarks>
public readonly record struct RemoteFolderPath
{
    /// <summary>The one folder name RFC 3501 mandates, and the only one that is case-insensitive.</summary>
    private const string InboxPath = "INBOX";

    private RemoteFolderPath(string value, char? hierarchyDelimiter)
    {
        this.Value = value;
        this.HierarchyDelimiter = hierarchyDelimiter;
    }

    /// <summary>Gets the server-advertised path.</summary>
    public string Value { get; }

    /// <summary>Gets the character the server separates hierarchy levels with, or <see langword="null" /> when the folder is flat or the server reported none.</summary>
    public char? HierarchyDelimiter { get; }

    /// <summary>Creates a path an operator wrote, whose hierarchy delimiter the server has not reported.</summary>
    /// <param name="value">The configured remote path, which is trimmed.</param>
    /// <returns>A validated path.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is blank or contains a control character.</exception>
    public static RemoteFolderPath Create(string value) => Create(value, hierarchyDelimiter: null);

    /// <summary>Creates a path an operator wrote, together with a hierarchy delimiter when one is known.</summary>
    /// <param name="value">The configured remote path, which is trimmed.</param>
    /// <param name="hierarchyDelimiter">The hierarchy delimiter, or <see langword="null" /> when none is known.</param>
    /// <returns>A validated path.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value" /> is blank, contains a control character, or is bounded by
    /// <paramref name="hierarchyDelimiter" />, and when <paramref name="hierarchyDelimiter" /> is whitespace or a
    /// control character.
    /// </exception>
    /// <remarks>
    /// Surrounding whitespace is removed, because it is padding in a configuration file rather than part of a name.
    /// A path the server itself advertised is built through <see cref="TryCreate" /> instead and keeps its text
    /// exactly.
    /// </remarks>
    public static RemoteFolderPath Create(string value, char? hierarchyDelimiter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return CreateFromExactText(value.Trim(), hierarchyDelimiter);
    }

    /// <summary>Creates a path from what a server advertised, keeping its text exactly and reporting rather than throwing when it names no folder.</summary>
    /// <param name="advertisedValue">The advertised path, which is used as-is.</param>
    /// <param name="hierarchyDelimiter">The server's hierarchy delimiter, or <see langword="null" /> when it reported none.</param>
    /// <param name="remoteFolderPath">The validated path, when the advertised one names a folder.</param>
    /// <returns><see langword="true" /> when the advertised path names a folder; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    /// <para>
    /// The text is not trimmed. IMAP permits a quoted mailbox name that begins or ends with a space, and trimming one
    /// would persist a path that selects a different mailbox or none at all — a folder that could then never be
    /// synchronized. Normalizing an operator's padding is a configuration concern and stays in
    /// <see cref="Create(string, char?)" />.
    /// </para>
    /// <para>
    /// Discovery reads whatever a server chooses to list, including entries that name no folder at all, such as a
    /// namespace root. One such entry must cost that entry and not the account's whole listing, which is why the
    /// rejection is a result here rather than an exception.
    /// </para>
    /// </remarks>
    public static bool TryCreate(string advertisedValue, char? hierarchyDelimiter, out RemoteFolderPath remoteFolderPath)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(advertisedValue);

            remoteFolderPath = CreateFromExactText(advertisedValue, hierarchyDelimiter);

            return true;
        }
        catch (ArgumentException)
        {
            remoteFolderPath = default;

            return false;
        }
    }

    /// <summary>Splits the path into its hierarchy levels.</summary>
    /// <returns>Every level in order, or the whole path as a single level when the server reported no delimiter.</returns>
    public IReadOnlyList<string> ToHierarchyLevels() =>
        this.HierarchyDelimiter is { } delimiter ? this.Value.Split(delimiter) : [this.Value];

    /// <inheritdoc />
    public override string ToString() => this.Value;

    /// <summary>Applies every rule that holds however the path was obtained, without altering its text.</summary>
    private static RemoteFolderPath CreateFromExactText(string value, char? hierarchyDelimiter)
    {
        if (value.Any(char.IsControl))
        {
            throw new ArgumentException("A remote folder path cannot contain control characters.", nameof(value));
        }

        if (hierarchyDelimiter is { } delimiter)
        {
            ValidateDelimitedPath(value, delimiter);
        }

        return new RemoteFolderPath(NormalizeInbox(value), hierarchyDelimiter);
    }

    /// <summary>Applies the one case rule IMAP itself defines, so a server writing <c>Inbox</c> names the same folder as one writing <c>INBOX</c>.</summary>
    private static string NormalizeInbox(string value) =>
        string.Equals(value, InboxPath, StringComparison.OrdinalIgnoreCase) ? InboxPath : value;

    /// <summary>Rejects a path whose delimiter placement means it does not name a folder.</summary>
    /// <remarks>
    /// A leading or trailing delimiter is an empty hierarchy level, which no server advertises and no operator means
    /// to write. Accepting it would let two spellings of the same folder resolve as two different remote paths and
    /// start a resolution generation that nothing on the server changed.
    /// </remarks>
    private static void ValidateDelimitedPath(string value, char hierarchyDelimiter)
    {
        if (char.IsWhiteSpace(hierarchyDelimiter) || char.IsControl(hierarchyDelimiter))
        {
            throw new ArgumentException(
                "A hierarchy delimiter must be a printable, non-whitespace character.",
                nameof(hierarchyDelimiter));
        }

        if (value.StartsWith(hierarchyDelimiter) || value.EndsWith(hierarchyDelimiter))
        {
            throw new ArgumentException(
                "A remote folder path cannot start or end with the hierarchy delimiter.",
                nameof(value));
        }
    }
}
