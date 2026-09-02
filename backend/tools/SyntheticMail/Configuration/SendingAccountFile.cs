// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.SyntheticMail.Generation;
using MimeKit;

namespace MailFathom.SyntheticMail.Configuration;

/// <summary>Where a run's accounts are read from, and the one place it can learn a password.</summary>
/// <remarks>
/// <para>
/// The credential never reaches an argument. A password typed on a command line lands in the shell history and in the
/// process list of a shared machine, and this repository is public enough that the pattern would be copied — so the
/// address and its password come from a local file that <c>.gitignore</c> covers as <c>*.local.json</c>, while the
/// recipient stays an argument, because the recipient is the part that changes per invocation. The file carries two
/// accounts: the sending one every batch submits as, and — for a run generating exchanges — the mailbox MailFathom
/// synchronizes, which that run reads over IMAP and appends to.
/// </para>
/// <para>
/// Every refusal here names the file and the key to set. A tool nobody has configured yet is the ordinary first
/// experience of it, so "what do I write, and where" is the whole content of the failure rather than something to go
/// and look up.
/// </para>
/// </remarks>
internal static class SendingAccountFile
{
    /// <summary>The name the file carries beside the built command.</summary>
    internal const string FileName = "synthetic-mail.local.json";

    /// <summary>The conventional submission port for a connection upgraded with <c>STARTTLS</c>.</summary>
    private const int SubmissionStartTlsPort = 587;

    /// <summary>The conventional submission port for a connection that handshakes TLS immediately.</summary>
    private const int SubmissionImplicitTlsPort = 465;

    /// <summary>The conventional IMAP port for a connection upgraded with <c>STARTTLS</c>.</summary>
    private const int ImapStartTlsPort = 143;

    /// <summary>The conventional IMAP port for a connection that handshakes TLS immediately.</summary>
    private const int ImapImplicitTlsPort = 993;

    /// <summary>Reports where the command looks when nothing was named.</summary>
    /// <returns>The absolute path of the credential file.</returns>
    /// <remarks>
    /// Beside the executable rather than relative to the working directory, so the command finds the same file however
    /// it was started. The project copies it there when a developer has written one.
    /// </remarks>
    internal static string DefaultPath() => Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>Reads the account, refusing anything incomplete.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The account, with every value checked.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path" /> is <see langword="null" />.</exception>
    /// <exception cref="SyntheticMailFailure">Thrown when the file is missing, unreadable, or incomplete, with a message naming what to write.</exception>
    internal static SendingAccount Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            throw new SyntheticMailFailure(
                $"No sending account is configured. Write '{path}' as {{ \"host\": \"…\", \"port\": 587, \"security\": \"StartTls\", \"address\": \"…\", \"password\": \"…\" }} and use a throwaway account: this tool fabricates mail and must never hold a credential that reaches anything else. The file is git-ignored.");
        }

        using var contents = OpenFile(path);

        return ReadFrom(contents, path);
    }

    /// <summary>Reads the account from an already-open file, which is where every check on its contents happens.</summary>
    /// <param name="contents">The file's contents.</param>
    /// <param name="origin">What the failures name, so a message points at a path rather than at a stream.</param>
    /// <returns>The account, with every value checked.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="SyntheticMailFailure">Thrown when the contents are not a complete sending account.</exception>
    /// <remarks>Separate from <see cref="Read" /> so every rule about what a credential file must say is exercised without a test writing one.</remarks>
    internal static SendingAccount ReadFrom(Stream contents, string origin)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(origin);

        var document = Deserialize(contents, origin);

        var host = Required(document.Host, "host", origin);
        var address = ParseAddress(Required(document.Address, "address", origin), "address", origin);
        var password = Required(document.Password, "password", origin);
        var security = ParseSecurity(document.Security, "security", origin, MailTransportSecurity.StartTls);
        var author = ParseAuthorIdentity(document.Author, origin);

        return new SendingAccount(
            host,
            ParsePort(document.Port, security, SubmissionStartTlsPort, SubmissionImplicitTlsPort, origin),
            security,
            address,
            string.IsNullOrWhiteSpace(document.UserName) ? address.Address : document.UserName,
            password,
            author);
    }

    /// <summary>Reads the mailbox MailFathom synchronizes, which only a run generating exchanges needs.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The mailbox, with every value checked.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path" /> is <see langword="null" />.</exception>
    /// <exception cref="SyntheticMailFailure">Thrown when the file is missing, unreadable, or carries no complete <c>mailbox</c> block, with a message naming what to write.</exception>
    internal static WatchedMailboxAccount ReadWatchedMailbox(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            throw new SyntheticMailFailure(
                $"No sending account is configured. Write '{path}' as {{ \"host\": \"…\", \"port\": 587, \"security\": \"StartTls\", \"address\": \"…\", \"password\": \"…\", \"mailbox\": {{ \"host\": \"…\", \"address\": \"…\", \"password\": \"…\" }} }} and use throwaway accounts for both: this tool fabricates mail and must never hold a credential that reaches anything else. The file is git-ignored.");
        }

        using var contents = OpenFile(path);

        return ReadWatchedMailboxFrom(contents, path);
    }

    /// <summary>Reads the watched mailbox from an already-open file, which is where every check on that block happens.</summary>
    /// <param name="contents">The file's contents.</param>
    /// <param name="origin">What the failures name, so a message points at a path rather than at a stream.</param>
    /// <returns>The mailbox, with every value checked.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="SyntheticMailFailure">Thrown when the contents carry no complete <c>mailbox</c> block.</exception>
    /// <remarks>Separate from <see cref="ReadWatchedMailbox" /> for the reason <see cref="ReadFrom" /> is separate from <see cref="Read" />.</remarks>
    internal static WatchedMailboxAccount ReadWatchedMailboxFrom(Stream contents, string origin)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(origin);

        var document = Deserialize(contents, origin).Mailbox
            ?? throw new SyntheticMailFailure(
                $"'mailbox' is not set in '{origin}'. Generating exchanges needs the mailbox MailFathom synchronizes, as {{ \"mailbox\": {{ \"host\": \"…\", \"port\": 993, \"security\": \"ImplicitTls\", \"address\": \"…\", \"password\": \"…\" }} }}, because a reply is built from the identifier that mailbox's server assigned.");

        var host = Required(document.Host, "mailbox.host", origin);
        var address = ParseAddress(Required(document.Address, "mailbox.address", origin), "mailbox.address", origin);
        var password = Required(document.Password, "mailbox.password", origin);
        var security = ParseSecurity(document.Security, "mailbox.security", origin, MailTransportSecurity.ImplicitTls);

        return new WatchedMailboxAccount(
            host,
            ParsePort(document.Port, security, ImapStartTlsPort, ImapImplicitTlsPort, origin),
            security,
            address,
            string.IsNullOrWhiteSpace(document.UserName) ? address.Address : document.UserName,
            password,
            string.IsNullOrWhiteSpace(document.SentFolder) ? null : document.SentFolder);
    }

    private static FileStream OpenFile(string path)
    {
        try
        {
            return File.OpenRead(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new SyntheticMailFailure($"'{path}' could not be opened: {failure.Message}", failure);
        }
    }

    private static SendingAccountDocument Deserialize(Stream contents, string origin)
    {
        try
        {
            return JsonSerializer.Deserialize(contents, SyntheticMailJsonContext.Default.SendingAccountDocument)
                ?? throw new SyntheticMailFailure($"'{origin}' holds no sending account.");
        }
        catch (Exception failure) when (failure is JsonException or IOException)
        {
            throw new SyntheticMailFailure($"'{origin}' could not be read as a sending account: {failure.Message}", failure);
        }
    }

    private static string Required(string? value, string key, string path) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new SyntheticMailFailure($"'{key}' is not set in '{path}'.")
            : value;

    private static MailboxAddress ParseAddress(string address, string key, string path) =>
        MailboxAddress.TryParse(address, out var parsed)
            ? parsed
            : throw new SyntheticMailFailure($"'{key}' in '{path}' is not a mail address.");

    /// <summary>Resolves how the connection carrying the credential is secured.</summary>
    /// <remarks>
    /// The definedness check sits beside the parse rather than after it, because <c>Enum.TryParse</c> also accepts a
    /// string of digits and answers with whatever number it holds — so <c>"security": "2"</c> would arrive as a value
    /// this enumeration never declared, and everything downstream treats anything that is not <c>ImplicitTls</c> as
    /// the upgrading option. A file naming something meaningless is refused however it spells it.
    /// </remarks>
    private static MailTransportSecurity ParseSecurity(
        string? security,
        string key,
        string path,
        MailTransportSecurity fallback)
    {
        if (string.IsNullOrWhiteSpace(security))
        {
            return fallback;
        }

        return Enum.TryParse<MailTransportSecurity>(security, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new SyntheticMailFailure(
                $"'{key}' in '{path}' is '{security}', which is not one of {string.Join(" or ", Enum.GetNames<MailTransportSecurity>())}. There is no unsecured option: the run authenticates with a password.");
    }

    private static SyntheticAuthorIdentity ParseAuthorIdentity(string? author, string path)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            return SyntheticAuthorIdentity.Fabricated;
        }

        return Enum.TryParse<SyntheticAuthorIdentity>(author, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new SyntheticMailFailure(
                $"'author' in '{path}' is '{author}', which is not one of {string.Join(" or ", Enum.GetNames<SyntheticAuthorIdentity>())}.");
    }

    /// <summary>Resolves the port, defaulting to the conventional one for the chosen security.</summary>
    /// <remarks>
    /// Defaulted rather than required, because the two conventions are fixed and a developer naming the wrong one for
    /// their own server gets a connection failure that says so. A written value is checked against the range MailKit
    /// documents for <c>ConnectAsync</c> before MailKit sees it: outside it the library throws
    /// <see cref="ArgumentOutOfRangeException" />, which is neither a transport failure the delivery layer translates
    /// nor a <see cref="SyntheticMailFailure" /> the runner reports, so a mistyped digit would surface as a stack trace
    /// where every other malformed value in this file produces one line naming the key.
    /// </remarks>
    private static int ParsePort(
        int? port,
        MailTransportSecurity security,
        int startTlsPort,
        int implicitTlsPort,
        string path)
    {
        if (port is not { } configured)
        {
            return security == MailTransportSecurity.ImplicitTls ? implicitTlsPort : startTlsPort;
        }

        return configured is >= 0 and <= 65535
            ? configured
            : throw new SyntheticMailFailure(
                $"'port' in '{path}' is {configured}, which is outside 0 to 65535.");
    }
}
