// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;

namespace MailFathom.Infrastructure.SensitiveContent.Secrets;

/// <summary>The rules MailFathom writes itself, where neither of the two corpora covers a shape mail carries.</summary>
/// <remarks>
/// Both corpora are written for source control, where a credential sits in a configuration file or a committed script.
/// Mail carries two shapes those miss: a connection string pasted into a thread so somebody can reproduce a failure,
/// and a link whose query string is the credential. Each is written here rather than tuned into a third-party corpus,
/// so refreshing either of those stays the mechanical diff it is meant to be.
/// </remarks>
internal static partial class MailFathomSecretRules
{
    /// <summary>The revision of this half of the corpus, which moves when an expression below changes.</summary>
    public const string CorpusRevision = "1";

    /// <summary>Every rule this half of the corpus contributes.</summary>
    public static IReadOnlyList<SecretRuleDefinition> Rules { get; } =
    [
        SecretRuleDefinition.Compile(
            SecretCategories.ConnectionString,
            "database-connection-uri-credential",
            DatabaseConnectionUriCredential()),
        SecretRuleDefinition.Compile(
            SecretCategories.ConnectionString,
            "connection-string-password-keyword",
            ConnectionStringPasswordKeyword()),
        SecretRuleDefinition.Compile(
            SecretCategories.CredentialUrl,
            "url-credential-query-parameter",
            UrlCredentialQueryParameter()),
    ];

    /// <remarks>
    /// The schemes are named rather than left open so that an ordinary <c>https</c> link with a colon in its path
    /// cannot reach this rule; the engine's own pattern covers a web URL carrying user information, and this one covers
    /// the service protocols a connection string is written in. The credential is the user-information half alone, so
    /// the host stays readable in the redacted text and a reader can still tell which system the line was about.
    /// </remarks>
    [GeneratedRegex(
        @"\b(?:postgres(?:ql)?|mysql|mariadb|mongodb(?:\+srv)?|redis(?:s)?|amqps?|mssql|sqlserver|jdbc:[a-z0-9]{1,20})://(?<refine>[^\s:@/]{1,128}:[^\s:@/]{1,256})@",
        SecretRegexEngine.MatchOptions | RegexOptions.IgnoreCase,
        SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex DatabaseConnectionUriCredential();

    /// <remarks>
    /// The trailing semicolon is what makes this a connection string rather than prose: a keyword-and-value list
    /// terminates every entry with one, and a sentence about a password does not. The value is bounded on both ends so
    /// an unterminated line cannot run to the end of a message.
    /// </remarks>
    [GeneratedRegex(
        @"(?:^|[;\s])(?:password|pwd|accountkey|sharedaccesskey)\s*=\s*(?<refine>[^;\s""'<>]{8,256});",
        SecretRegexEngine.MatchOptions | RegexOptions.IgnoreCase,
        SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ConnectionStringPasswordKeyword();

    /// <remarks>
    /// A link whose query names a credential is the one place a URL is worth redacting in part rather than whole: the
    /// address stays, so a reader knows what was linked, and only the value the parameter names is replaced. The
    /// parameter names are the ones a service actually issues links with; a bare <c>id</c> or <c>ref</c> is left alone,
    /// because a mailbox is full of them and none of them is a credential.
    /// </remarks>
    [GeneratedRegex(
        @"[?&](?:access_token|refresh_token|id_token|auth_token|authtoken|api[_-]?key|apikey|client_secret|private_token|session_token|signature|sig|sas|token|secret)=(?<refine>[A-Za-z0-9._~+/%-]{16,512})",
        SecretRegexEngine.MatchOptions | RegexOptions.IgnoreCase,
        SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex UrlCredentialQueryParameter();
}
