// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.UnitTests.SensitiveContent.Secrets;

/// <summary>Builds credentials that match the corpus without any of them appearing in this repository.</summary>
/// <remarks>
/// <para>
/// Every value here is invented and belongs to nobody, but a test that spelled one out as a literal would still put a
/// string shaped exactly like a live credential into a public repository — where the platform's own scanning reads it,
/// reports it, and can refuse the push that carries it. Assembling each one from its prefix and a filler at run time
/// keeps the shape out of the committed text while the value the test matches against is identical.
/// </para>
/// <para>
/// The fillers are deliberately monotonous. What a rule matches is a shape rather than randomness, so a repeated
/// character proves the rule as well as a random run would and reads as obviously synthetic.
/// </para>
/// </remarks>
internal static class SyntheticSecrets
{
    private const string WebhookIdentifier = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    /// <summary>A personal access token of the shape one hosted forge issues.</summary>
    public static string ProviderToken { get; } = "gh" + "p_" + new string('a', 36);

    /// <summary>An access key identifier of the shape one cloud platform issues.</summary>
    public static string CloudAccessKey { get; } = "AK" + "IA" + new string('B', 16);

    /// <summary>A hosting provider's personal access token, whose rule ends in a lookahead over hexadecimal.</summary>
    public static string HostingProviderToken { get; } = "dop" + "_v1_" + new string('a', 64);

    /// <summary>A payment platform's test key, whose rule ends in a lookahead over an alphanumeric run.</summary>
    public static string PaymentPlatformKey { get; } = "sk" + "_test_" + new string('b', 32);

    /// <summary>A package registry's access token, whose rule reads case-insensitively.</summary>
    public static string PackageRegistryToken { get; } = "npm" + "_" + new string('c', 36);

    /// <summary>A mail platform's key, whose own alphabet holds the full stop that would otherwise end it.</summary>
    public static string MailPlatformKey { get; } = "SG" + "." + new string('d', 22) + "." + new string('e', 43);

    /// <summary>A second cloud platform's key, whose rule ends in a lookahead over a word character.</summary>
    public static string CloudServiceKey { get; } = "AI" + "za" + new string('f', 35);

    /// <summary>An edge platform's token, in the first of the three forms its one rule alternates between.</summary>
    public static string EdgePlatformToken { get; } = "f" + "o1_" + new string('a', 43);

    /// <summary>The same platform's second form, spelled in base64 where the first is spelled in word characters.</summary>
    public static string EdgePlatformBase64Token { get; } = "fm" + "1a_" + new string('b', 100) + "==";

    /// <summary>A model provider's key in its legacy form, which is alphanumeric throughout.</summary>
    public static string ModelProviderKey { get; } =
        "sk" + "-" + new string('c', 20) + "T3BlbkFJ" + new string('d', 20);

    /// <summary>The same provider's project form, whose alphabet also carries an underscore and a hyphen.</summary>
    public static string ModelProviderProjectKey { get; } =
        "sk" + "-proj-" + new string('e', 58) + "T3BlbkFJ" + new string('f', 58);

    /// <summary>A secret store's service token in its current form.</summary>
    public static string SecretStoreToken { get; } = "hvs" + "." + new string('g', 90);

    /// <summary>The same store's legacy form, alphanumeric where the current one takes a hyphen too.</summary>
    public static string SecretStoreLegacyToken { get; } = "s" + "." + new string('h', 24);

    /// <summary>A database platform's API token, whose own alphabet carries the full stop that can follow it.</summary>
    public static string DatabasePlatformApiToken { get; } = "pscale" + "_tkn_" + new string('i', 32);

    /// <summary>The same platform's OAuth token, which its rule reads case-sensitively.</summary>
    public static string DatabasePlatformOauthToken { get; } = "pscale" + "_oauth_" + new string('j', 32);

    /// <summary>The same platform's password, whose rule reads case-insensitively throughout.</summary>
    public static string DatabasePlatformPassword { get; } = "pscale" + "_pw_" + new string('k', 32);

    /// <summary>A private key block, armoured the way an agent or a certificate tool emits one.</summary>
    public static string PrivateKey { get; } =
        "-----BEGIN RSA PRIVATE KEY-----\n"
        + string.Concat(Enumerable.Repeat("QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVoK\n", 4))
        + "-----END RSA PRIVATE KEY-----";

    /// <summary>A JSON Web Token, in the three dot-separated parts one always has.</summary>
    public static string JsonWebToken { get; } =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9"
        + "." + "eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkFkYSJ9"
        + "." + new string('c', 32);

    /// <summary>The user information a database connection string carries, which is the part worth replacing.</summary>
    public static string ConnectionStringCredential { get; } = "reporting:" + new string('d', 24);

    /// <summary>A connection string for a database, carrying the credential it connects with.</summary>
    public static string ConnectionString { get; } =
        "postgres" + "ql://" + ConnectionStringCredential + "@db.example.invalid:5432/analytics";

    /// <summary>The token a link's query string names, which is the part worth replacing.</summary>
    public static string CredentialUrlToken { get; } = new('e', 40);

    /// <summary>A link whose query string is the credential.</summary>
    public static string CredentialUrl { get; } =
        "https://example.invalid/export?format=csv&access_token=" + CredentialUrlToken;

    /// <summary>The incoming-webhook URL of a chat platform, the whole of which is the posting credential.</summary>
    /// <remarks>
    /// Nothing here can be kept readable. Anyone holding the URL can post into the channel, so unlike a database link
    /// or a tracked link there is no part of it worth leaving behind for a reader to orient by.
    /// </remarks>
    public static string ChannelWebhookUrl { get; } =
        "https://outlook" + ".webhook.office.com/webhookb2/" + WebhookIdentifier + "@" + WebhookIdentifier
        + "/IncomingWebhook/" + new string('f', 32) + "/" + WebhookIdentifier;

    /// <summary>A short-lived cloud model-service key, whose constant opening is followed by the credential itself.</summary>
    public static string ShortLivedModelServiceKey { get; } =
        "bedrock-api-key-" + "YmVkcm9jay5hbWF6b25hd3MuY29t" + new string('Z', 64) + "==";

    /// <summary>Thirty-two bytes of base64, dense enough for the entropy heuristic to report it.</summary>
    public static string HighEntropyString { get; } = "Zq7ZkR3vXp8L" + "mT2wYc5NbJ6hQ4sD9fG1a" + "E0uIoPrWxV=";

    /// <summary>Text a mailbox is full of that no rule may report.</summary>
    /// <remarks>
    /// Each line is something the corpus comes close to and must not match: a message identifier, a commit hash, a
    /// tracking link, a quoted price, a sentence about a password, and a base64 run too repetitive to be a credential.
    /// </remarks>
    public static IReadOnlyList<string> FalsePositives { get; } =
    [
        "Message-ID: <CAF=1a2b3c4d5e6f7890abcdef1234567890@mail.example.invalid>",
        "The fix landed in 9f2c1ab7e45d0836ba91cc57de204f6a8b3e1d92, please rebase onto it.",
        "https://example.invalid/newsletter/click?campaign=2026-08-summer&position=3",
        "Invoice 2026/08/0142 for 1 240,00 EUR is attached; the reference is INV-20260812-0142.",
        "I reset the password on the staging box, it is in the vault under platform/staging.",
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
        "Please review https://github.example.invalid/platform/mail/pull/4471#discussion_r1234567890",
        "Session begins at 09:30 CEST; the room code is 4471 and the dial-in is +48 000 000 000.",
    ];
}
