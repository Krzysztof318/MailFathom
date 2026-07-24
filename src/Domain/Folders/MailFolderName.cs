// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Domain.Folders;

/// <summary>Represents an IMAP folder name as configured or advertised by the server.</summary>
public readonly record struct MailFolderName
{
    private MailFolderName(string value) => this.Value = value;

    /// <summary>Gets the folder name.</summary>
    public string Value { get; }

    /// <summary>Creates a folder name.</summary>
    /// <param name="value">The IMAP folder name.</param>
    /// <returns>A validated folder name.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is blank.</exception>
    public static MailFolderName Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new MailFolderName(value.Trim());
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
