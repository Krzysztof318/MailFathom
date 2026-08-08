// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.SyntheticMail.Generation;
using MimeKit;

namespace MailFathom.SyntheticMail.Configuration;

/// <summary>Where the sending account is read from, and the one place a run can learn a password.</summary>
/// <remarks>
/// <para>
/// The credential never reaches an argument. A password typed on a command line lands in the shell history and in the
/// process list of a shared machine, and this repository is public enough that the pattern would be copied — so the
/// address and its password come from a local file that <c>.gitignore</c> covers as <c>*.local.json</c>, while the
/// recipient stays an argument, because the recipient is the part that changes per invocation.
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
        var address = ParseAddress(Required(document.Address, "address", origin), origin);
        var password = Required(document.Password, "password", origin);
        var security = ParseSecurity(document.Security, origin);
        var author = ParseAuthorIdentity(document.Author, origin);

        return new SendingAccount(
            host,
            ParsePort(document.Port, security, origin),
            security,
            address,
            string.IsNullOrWhiteSpace(document.UserName) ? address.Address : document.UserName,
            password,
            author);
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

    private static MailboxAddress ParseAddress(string address, string path) =>
        MailboxAddress.TryParse(address, out var parsed)
            ? parsed
            : throw new SyntheticMailFailure($"'address' in '{path}' is not a mail address.");

    private static SmtpTransportSecurity ParseSecurity(string? security, string path)
    {
        if (string.IsNullOrWhiteSpace(security))
        {
            return SmtpTransportSecurity.StartTls;
        }

        return Enum.TryParse<SmtpTransportSecurity>(security, ignoreCase: true, out var parsed)
            ? parsed
            : throw new SyntheticMailFailure(
                $"'security' in '{path}' is '{security}', which is not one of {string.Join(" or ", Enum.GetNames<SmtpTransportSecurity>())}. There is no unsecured option: the run authenticates with a password.");
    }

    private static SyntheticAuthorIdentity ParseAuthorIdentity(string? author, string path)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            return SyntheticAuthorIdentity.Fabricated;
        }

        return Enum.TryParse<SyntheticAuthorIdentity>(author, ignoreCase: true, out var parsed)
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
    private static int ParsePort(int? port, SmtpTransportSecurity security, string path)
    {
        if (port is not { } configured)
        {
            return security == SmtpTransportSecurity.ImplicitTls ? 465 : 587;
        }

        return configured is >= 0 and <= 65535
            ? configured
            : throw new SyntheticMailFailure(
                $"'port' in '{path}' is {configured}, which is outside 0 to 65535.");
    }
}
