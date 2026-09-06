// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Sockets;
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
/// synchronizes, which that run reads over IMAP and appends to. A checkout that wrote no such file reads the same
/// keys out of the machine's user-secrets store instead, which <see cref="ConfigurationSource" /> is about.
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

    /// <summary>The plain SMTP port, which is where a test mail server accepts mail when nothing was submitted to it over 587.</summary>
    private const int SubmissionUnsecuredPort = 25;

    /// <summary>The conventional IMAP port for a connection that is never encrypted, which is the plain port <c>STARTTLS</c> upgrades from.</summary>
    private const int ImapUnsecuredPort = 143;

    /// <summary>The names that reach a mail server on the machine running this command, or the container host beside it.</summary>
    /// <remarks>
    /// The two container names are the aliases Docker and Podman publish for the host from inside a container, so a
    /// run started in one reaches a mail server the developer started outside it. Anything else has to resolve to an
    /// address to be judged, which is what <see cref="IsLocalOrContainerHost" /> does instead of trusting a name.
    /// </remarks>
    private static readonly string[] LocalHostNames =
    [
        "localhost",
        "host.docker.internal",
        "host.containers.internal",
    ];

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

        var configured = ConfigurationSource.Open(path)
            ?? throw new SyntheticMailFailure(
                $"No sending account is configured. Write '{path}' as {{ \"host\": \"…\", \"port\": 587, \"security\": \"StartTls\", \"address\": \"…\", \"password\": \"…\" }}, or set the same keys with `dotnet user-secrets set --project backend/tools/SyntheticMail`, and use a throwaway account: this tool fabricates mail and must never hold a credential that reaches anything else. The file is git-ignored.");

        using var contents = configured.Contents;

        return ReadFrom(contents, configured.Origin);
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

        RefuseUnsecuredRemoteHost(host, security, "host", "security", origin);

        return new SendingAccount(
            host,
            ParsePort(document.Port, security, SubmissionStartTlsPort, SubmissionImplicitTlsPort, SubmissionUnsecuredPort, "port", origin),
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

        var configured = ConfigurationSource.Open(path)
            ?? throw new SyntheticMailFailure(
                $"No sending account is configured. Write '{path}' as {{ \"host\": \"…\", \"port\": 587, \"security\": \"StartTls\", \"address\": \"…\", \"password\": \"…\", \"mailbox\": {{ \"host\": \"…\", \"address\": \"…\", \"password\": \"…\" }} }}, or set the same keys with `dotnet user-secrets set --project backend/tools/SyntheticMail`, and use throwaway accounts for both: this tool fabricates mail and must never hold a credential that reaches anything else. The file is git-ignored.");

        using var contents = configured.Contents;

        return ReadWatchedMailboxFrom(contents, configured.Origin);
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

        RefuseUnsecuredRemoteHost(host, security, "mailbox.host", "mailbox.security", origin);

        return new WatchedMailboxAccount(
            host,
            ParsePort(document.Port, security, ImapStartTlsPort, ImapImplicitTlsPort, ImapUnsecuredPort, "mailbox.port", origin),
            security,
            address,
            string.IsNullOrWhiteSpace(document.UserName) ? address.Address : document.UserName,
            password,
            string.IsNullOrWhiteSpace(document.SentFolder) ? null : document.SentFolder);
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

    /// <summary>Resolves an enumeration value written by name, refusing anything that is not one.</summary>
    /// <remarks>
    /// <c>Enum.TryParse</c> also accepts a string of digits and answers with whatever number it holds, so a check that
    /// the result is defined is not the same question: <c>"security": "2"</c> is a defined value the moment a third
    /// member is declared, and it would then select an unsecured connection without the file ever saying so. What a
    /// file is allowed to write is a name, which is what this compares against.
    /// </remarks>
    private static bool TryParseName<TValue>(string written, out TValue parsed)
        where TValue : struct, Enum =>
        Enum.TryParse(written, ignoreCase: true, out parsed)
            && Enum.GetNames<TValue>().Contains(written.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves how the connection carrying the credential is secured.</summary>
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

        return TryParseName<MailTransportSecurity>(security, out var parsed)
            ? parsed
            : throw new SyntheticMailFailure(
                $"'{key}' in '{path}' is '{security}', which is not one of {string.Join(" or ", Enum.GetNames<MailTransportSecurity>())}. There is no opportunistic option: the run authenticates with a password, so a connection either secures itself or is named '{nameof(MailTransportSecurity.Unsecured)}' against a local test server.");
    }

    /// <summary>Refuses an unsecured connection to anything but a mail server on this machine or the container host beside it.</summary>
    /// <remarks>
    /// The refusal is here rather than in the transport because this is where a value is judged, and because a host is
    /// the only thing that decides whether the credential is at risk. It is checked on the host as written rather than
    /// on what it resolves to: a lookup is a network call this reader must not make, and a name that resolves to a
    /// loopback address today can resolve elsewhere tomorrow, so an unrecognized name is refused rather than trusted.
    /// </remarks>
    private static void RefuseUnsecuredRemoteHost(
        string host,
        MailTransportSecurity security,
        string hostKey,
        string securityKey,
        string path)
    {
        if (security != MailTransportSecurity.Unsecured || IsLocalOrContainerHost(host))
        {
            return;
        }

        throw new SyntheticMailFailure(
            $"'{securityKey}' in '{path}' is '{nameof(MailTransportSecurity.Unsecured)}', which sends the password in the clear, and '{hostKey}' is '{host}', which is neither a loopback nor a container address. It exists for a mail server running beside this command — 'localhost', an address in 127.0.0.0/8 or ::1, a private range a container bridge hands out, or 'host.docker.internal' — so name one of those or secure the connection with {nameof(MailTransportSecurity.StartTls)} or {nameof(MailTransportSecurity.ImplicitTls)}.");
    }

    /// <summary>Reports whether a host as written reaches this machine or the container host beside it.</summary>
    private static bool IsLocalOrContainerHost(string host) =>
        IPAddress.TryParse(host, out var address)
            ? IPAddress.IsLoopback(address) || IsContainerAddress(address)
            : LocalHostNames.Contains(host, StringComparer.OrdinalIgnoreCase)
                || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reports whether an address is one a container runtime hands out rather than one that leaves the machine.</summary>
    /// <remarks>
    /// The private ranges are what a bridge network allocates — Docker's default is in 172.16.0.0/12 and Podman's in
    /// 10.0.0.0/8 — and the link-local ones are what a host with no allocation falls back to. Every one of them is
    /// still a network a password would travel on, which is why this permits an unsecured connection only in a tool
    /// that fabricates its own mail and holds a throwaway credential by construction.
    /// </remarks>
    private static bool IsContainerAddress(IPAddress address) =>
        address.AddressFamily switch
        {
            AddressFamily.InterNetwork => address.GetAddressBytes() is
                [10, ..] or [172, >= 16 and <= 31, ..] or [192, 168, ..] or [169, 254, ..],
            AddressFamily.InterNetworkV6 => address.IsIPv6LinkLocal
                || address.IsIPv6UniqueLocal
                || (address.IsIPv4MappedToIPv6 && IsContainerAddress(address.MapToIPv4())),
            _ => false,
        };

    private static SyntheticAuthorIdentity ParseAuthorIdentity(string? author, string path)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            return SyntheticAuthorIdentity.Fabricated;
        }

        return TryParseName<SyntheticAuthorIdentity>(author, out var parsed)
            ? parsed
            : throw new SyntheticMailFailure(
                $"'author' in '{path}' is '{author}', which is not one of {string.Join(" or ", Enum.GetNames<SyntheticAuthorIdentity>())}.");
    }

    /// <summary>Resolves the port, defaulting to the conventional one for the chosen security.</summary>
    /// <remarks>
    /// Defaulted rather than required, because each convention is fixed and a developer naming the wrong one for
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
        int unsecuredPort,
        string key,
        string path)
    {
        if (port is not { } configured)
        {
            return security switch
            {
                MailTransportSecurity.ImplicitTls => implicitTlsPort,
                MailTransportSecurity.Unsecured => unsecuredPort,
                _ => startTlsPort,
            };
        }

        return configured is >= 0 and <= 65535
            ? configured
            : throw new SyntheticMailFailure(
                $"'{key}' in '{path}' is {configured}, which is outside 0 to 65535.");
    }
}
