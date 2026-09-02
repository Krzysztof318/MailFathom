// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Administration;

/// <summary>Names the top-level sections a MailFathom deployment's own configuration is written under.</summary>
/// <remarks>
/// <para>
/// This exists for one question a reading has to answer and nothing else can: whether a path the composed configuration
/// carries is a setting of this deployment or a variable of the process it happens to run in. The framework composes an
/// unprefixed environment provider, and that provider turns <em>every</em> environment variable into a configuration
/// path — so <c>OPENAI_API_KEY</c>, <c>GH_PAT</c>, and a database password some orchestrator injected under a name
/// nobody here chose are all keys <see cref="EffectiveSettingsReader" /> would otherwise answer with, in full, to a
/// caller holding the reading permission alone. <see cref="SettingRedaction" /> cannot help there: it decides by
/// MailFathom's own naming rule and MailFathom's own bootstrap list, and neither says anything about a name this
/// project never chose.
/// </para>
/// <para>
/// The list is written out rather than discovered, because discovering it would mean reflecting over every options
/// class on a request path to answer a question whose answer changes only when this repository changes. What keeps it
/// honest is a test rather than a mechanism: <c>PublicSurfaces.UnitTests</c> renders the configuration key set from the
/// <c>SectionName</c> constant of every bound options class and fails when a section it names is missing here. A
/// section added without a line here therefore fails a build rather than quietly withholding its own settings.
/// </para>
/// <para>
/// <c>Logging</c> and <c>ConnectionStrings</c> are framework-shaped rather than MailFathom's, which is why the key set
/// leaves them out; they are named here anyway, because an operator reading a deployment's settings means those too and
/// a deployment supplies them exactly as it supplies the rest. <c>Accounts</c> is MailFathom's and is absent from the
/// key set for a different reason: it is routed out of the root document into the owner-account store, so no options
/// class binds it.
/// </para>
/// </remarks>
internal static class MailFathomConfigurationSections
{
    private static readonly string[] Named =
    [
        "Accounts",
        "AdminEndpoint",
        "Chat",
        "ClientEndpoint",
        "ConfigurationSources",
        "ConnectionLimits",
        "ConnectionStrings",
        "ContentStorage",
        "DataEncryption",
        "Deployment",
        "EmailContent",
        "EmbeddingBackfill",
        "Embeddings",
        "HealthEndpoints",
        "Jobs",
        "Logging",
        "MailAnswering",
        "MailboxSearch",
        "MailDelivery",
        "MailExtractionBackfill",
        "MailRules",
        "MailSynchronization",
        "McpEndpoint",
        "Persistence",
        "Resilience",
        "ReverseProxy",
        "Secrets",
        "SensitiveContent",
        "SpamClassification",
    ];

    /// <summary>Reports whether a configuration path sits under a section this deployment's configuration defines.</summary>
    /// <param name="configurationPath">The colon-delimited configuration path.</param>
    /// <returns><see langword="true" /> when the path's first segment names a MailFathom section.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurationPath" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The first segment alone, because everything beneath a section is that section's whatever it is called: a key an
    /// operator chose, a list position, a nested object. A path with no colon in it is a bare environment variable in
    /// every case that reaches here, since every MailFathom setting is a key within a section.
    /// </remarks>
    internal static bool Name(string configurationPath)
    {
        ArgumentNullException.ThrowIfNull(configurationPath);

        var boundary = configurationPath.IndexOf(':', StringComparison.Ordinal);
        var section = boundary < 0 ? configurationPath : configurationPath[..boundary];

        return Named.Contains(section, StringComparer.OrdinalIgnoreCase);
    }
}
