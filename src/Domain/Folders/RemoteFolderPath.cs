// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Domain.Folders;

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

    /// <summary>Creates a path whose hierarchy delimiter the server has not reported, such as one written in configuration.</summary>
    /// <param name="value">The remote path.</param>
    /// <returns>A validated path.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is blank or contains a control character.</exception>
    public static RemoteFolderPath Create(string value) => Create(value, hierarchyDelimiter: null);

    /// <summary>Creates a path together with the hierarchy delimiter the server advertised for it.</summary>
    /// <param name="value">The remote path.</param>
    /// <param name="hierarchyDelimiter">The server's hierarchy delimiter, or <see langword="null" /> when it reported none.</param>
    /// <returns>A validated path.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value" /> is blank, contains a control character, or is bounded by
    /// <paramref name="hierarchyDelimiter" />, and when <paramref name="hierarchyDelimiter" /> is whitespace or a
    /// control character.
    /// </exception>
    public static RemoteFolderPath Create(string value, char? hierarchyDelimiter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmed = value.Trim();
        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("A remote folder path cannot contain control characters.", nameof(value));
        }

        if (hierarchyDelimiter is { } delimiter)
        {
            ValidateDelimitedPath(trimmed, delimiter);
        }

        return new RemoteFolderPath(NormalizeInbox(trimmed), hierarchyDelimiter);
    }

    /// <summary>Creates a path from what a server advertised, reporting rather than throwing when it is unusable.</summary>
    /// <param name="value">The advertised path.</param>
    /// <param name="hierarchyDelimiter">The server's hierarchy delimiter, or <see langword="null" /> when it reported none.</param>
    /// <param name="remoteFolderPath">The validated path, when the advertised one is usable.</param>
    /// <returns><see langword="true" /> when the advertised path names a folder; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    /// Discovery reads whatever a server chooses to list, including entries that name no folder at all, such as a
    /// namespace root. One such entry must cost that entry and not the account's whole listing, which is why the
    /// rejection is a result here rather than the exception <see cref="Create(string, char?)" /> raises for a value an
    /// operator wrote.
    /// </remarks>
    public static bool TryCreate(string value, char? hierarchyDelimiter, out RemoteFolderPath remoteFolderPath)
    {
        try
        {
            remoteFolderPath = Create(value, hierarchyDelimiter);

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
