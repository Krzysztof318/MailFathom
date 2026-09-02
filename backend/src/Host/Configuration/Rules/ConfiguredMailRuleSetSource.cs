// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Conditions;

namespace MailFathom.Host.Configuration.Rules;

/// <summary>Hands out the rule set the published configuration declares, mapping it once per configuration it adopts.</summary>
/// <remarks>
/// <para>
/// Reading the published snapshot rather than the bound options is what keeps a refused reload from reaching a pass:
/// the snapshot only publishes a candidate it has proven usable, so the rule set handed out here was compiled from a
/// configuration that validated in full.
/// </para>
/// <para>
/// The mapping is remembered against the snapshot instance it came from, so a pass costs one reference comparison
/// rather than a parse of every condition. A reload publishes a different instance, which is what makes the next pass
/// map again — and a pass that has already taken a set keeps it, because the reload contract for rules is that an edit
/// reaches the next pass and never one already running.
/// </para>
/// </remarks>
internal sealed class ConfiguredMailRuleSetSource : IMailRuleSetSource
{
    private readonly ISettingsSnapshot<MailRulesOptions> settings;
    private readonly IMailRuleConditionCompiler compiler;
    private readonly Lock mapping = new();

    private MailRulesOptions? mappedFrom;

    /// <summary>Initializes the source over the published rule configuration.</summary>
    /// <param name="settings">The published configuration, which is only ever a candidate that validated.</param>
    /// <param name="compiler">Reads each condition against the fact surface.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public ConfiguredMailRuleSetSource(
        ISettingsSnapshot<MailRulesOptions> settings,
        IMailRuleConditionCompiler compiler)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(compiler);

        this.settings = settings;
        this.compiler = compiler;
    }

    /// <inheritdoc />
    public MailRuleSet Current
    {
        get
        {
            var published = this.settings.Current;

            lock (this.mapping)
            {
                if (!ReferenceEquals(published, this.mappedFrom) || field is null)
                {
                    field = MailRuleSetMapper.Map(published, this.compiler);
                    this.mappedFrom = published;
                }

                return field;
            }
        }
    }
}
