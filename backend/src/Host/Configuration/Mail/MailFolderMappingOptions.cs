// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Folders;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Configures one folder: where it is, what it is for, and what it takes part in.</summary>
/// <remarks>
/// <para>
/// Configuration names folders by alias only. Either the operator writes the server's own path, or — preferably —
/// names a special-use role and lets discovery find whichever folder carries it, which is what makes an account on a
/// server with non-English folder names work with no configuration of its own.
/// </para>
/// <para>
/// <see cref="SpecialUse" /> is one key doing both jobs. Written alone it finds the folder and labels it; written
/// beside a <see cref="RemotePath" /> it only labels, which is how a folder on a server that advertises no attribute
/// for it is still the folder a feature asking for this account's junk mail is given. One key rather than two, because
/// two keys that both spell <c>Junk</c> would eventually be written disagreeing.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailFolderMappingOptions : IValidatableObject
{
    /// <summary>Gets or sets the stable operator-facing folder name.</summary>
    [Required]
    public string Alias { get; set; } = string.Empty;

    /// <summary>Gets or sets the server-advertised path the alias names, which the folder is found by when it is written.</summary>
    public string? RemotePath { get; set; }

    /// <summary>Gets or sets the role the folder plays, which also finds the folder when no <see cref="RemotePath" /> is written.</summary>
    /// <remarks>
    /// A role is unique within an account, so two folders of one account naming the same role fail startup. Naming none
    /// is ordinary: a folder needs a role only when something is going to ask for it by one.
    /// </remarks>
    public string? SpecialUse { get; set; }

    /// <summary>Gets or sets whether the folder's mail is mirrored locally, which every mapped folder does unless it is turned off.</summary>
    /// <remarks>
    /// It is nullable so that leaving it out and writing <c>true</c> stay distinguishable. Both mirror the folder, but
    /// only the second is an operator asking for something, which is what validation needs to tell a contradiction from
    /// a default.
    /// </remarks>
    public bool? Synchronize { get; set; }

    /// <summary>Gets or sets whether the mirrored content is cut into passages and embedded.</summary>
    public bool? GenerateEmbeddings { get; set; }

    /// <summary>Gets or sets whether MCP tools may list, search, read, or answer from the folder.</summary>
    public bool? VisibleToTools { get; set; }

    /// <summary>Gets or sets whether MailFathom may create the folder when the server advertises none at <see cref="RemotePath" />.</summary>
    /// <remarks>
    /// It defaults to <see langword="false" />, unlike the three switches above, because it authorizes an act against
    /// the operator's mail server rather than withdrawing an existing folder from something MailFathom does locally.
    /// Leaving it out therefore keeps a mistyped path reporting itself as an alias that resolves to nothing.
    /// </remarks>
    public bool? CreateIfMissing { get; set; }

    /// <summary>Gets the special-use role this folder names, or <see langword="null" /> when it names a remote path instead.</summary>
    /// <remarks>
    /// Read from what the operator wrote rather than from what the server advertises, which is the only source available
    /// before a folder has ever been resolved. An account whose junk folder is configured by path therefore has no junk
    /// role here, and mail in it is listed like any other folder's — which is the honest answer, since nothing told
    /// MailFathom that the path is where junk goes.
    /// </remarks>
    internal MailFolderSpecialUse? ConfiguredSpecialUse =>
        TryParseSpecialUse(this.SpecialUse, out var specialUse) ? specialUse : null;

    /// <summary>Gets what this configured folder takes part in, with every unset switch reading as its default.</summary>
    internal MailFolderParticipation Participation => MailFolderParticipation.Create(
        this.Synchronize ?? true,
        this.GenerateEmbeddings ?? true,
        this.VisibleToTools ?? true);

    /// <summary>Builds the domain mapping this configured folder expresses.</summary>
    /// <returns>The mapping folder resolution reads.</returns>
    /// <exception cref="InvalidOperationException">Thrown when configuration that passed startup validation names neither a remote path nor a supported special-use role.</exception>
    internal MailFolderMapping CreateMapping()
    {
        var alias = MailFolderAlias.Create(this.Alias);
        var namesRole = TryParseSpecialUse(this.SpecialUse, out var specialUse);

        if (!string.IsNullOrWhiteSpace(this.RemotePath))
        {
            return MailFolderMapping.ToRemotePath(
                alias,
                RemoteFolderPath.Create(this.RemotePath),
                this.Participation,
                this.CreateIfMissing ?? false,
                namesRole ? specialUse : null);
        }

        if (namesRole)
        {
            return MailFolderMapping.ToSpecialUse(alias, specialUse, this.Participation);
        }

        throw new InvalidOperationException(
            $"Folder alias '{this.Alias}' names neither a remote path nor a supported special-use role.");
    }

    /// <summary>Gets the role this folder plays, or <see langword="null" /> when it names none this system supports.</summary>
    /// <remarks>
    /// Read without raising, so the rule that refuses two folders of one account sharing a role can group by it while a
    /// misspelled role is still being reported against the entry that wrote it.
    /// </remarks>
    internal MailFolderSpecialUse? DeclaredRole => TryParseSpecialUse(this.SpecialUse, out var specialUse)
        ? specialUse
        : null;

    internal IEnumerable<ValidationResult> ValidateForSynchronization()
    {
        if (string.IsNullOrWhiteSpace(this.Alias))
        {
            yield return new ValidationResult("Configured folder aliases must be non-empty.", [nameof(this.Alias)]);
        }

        // An alias is written wherever a folder is named, and a role is written there too behind this prefix. An alias
        // carrying it would therefore be a folder nothing could name: every caller writing it would reach the role
        // instead, and the role would answer for whichever folder actually plays it.
        if (this.Alias?.TrimStart().StartsWith(MailFolderReference.RoleScheme, StringComparison.OrdinalIgnoreCase)
            is true)
        {
            yield return new ValidationResult(
                $"Folder alias '{this.Alias}' begins with '{MailFolderReference.RoleScheme}', which is how a folder is named by the role it plays rather than by its alias. Choose an alias that does not begin with it.",
                [nameof(this.Alias)]);
        }

        var namesRemotePath = !string.IsNullOrWhiteSpace(this.RemotePath);
        var namesSpecialUse = !string.IsNullOrWhiteSpace(this.SpecialUse);

        // At least one rather than exactly one: the two answer different questions once a role is a property of the
        // folder, so writing both is a folder found by path and labelled with a role. Writing neither still leaves
        // nothing that could find the folder at all.
        if (!namesRemotePath && !namesSpecialUse)
        {
            yield return new ValidationResult(
                $"Folder alias '{this.Alias}' must name at least one of RemotePath and SpecialUse.",
                [nameof(this.RemotePath), nameof(this.SpecialUse)]);

            yield break;
        }

        if (namesSpecialUse && !TryParseSpecialUse(this.SpecialUse, out _))
        {
            yield return new ValidationResult(
                $"Folder alias '{this.Alias}' names special-use role '{this.SpecialUse}', which is not supported. Supported roles are {string.Join(", ", Enum.GetNames<MailFolderSpecialUse>())}.",
                [nameof(this.SpecialUse)]);
        }

        // Only a role-only mapping is refused. A folder found by path and labelled with a role names the path the
        // folder would be created at, so the objection — that a folder which does not exist advertises no role — does
        // not apply to it.
        if (!namesRemotePath && namesSpecialUse && this.CreateIfMissing is true)
        {
            yield return new ValidationResult(
                $"Folder alias '{this.Alias}' asks for its folder to be created while being found by a special-use role, and a folder that does not exist advertises no role. Name the path the folder is to be created at in 'RemotePath'.",
                [nameof(this.CreateIfMissing)]);
        }

        // The one role no server ever advertises. RFC 6154 declares no outbox attribute and MailFathom does not invent
        // one from a folder's name, so a mapping that expects discovery to find it would resolve to nothing and the
        // deployment would learn that from mail it never saw mirrored. Written beside a path the role is a label like
        // any other, which is exactly how an operator says which of their folders holds what is waiting to go out.
        if (!namesRemotePath && this.DeclaredRole == MailFolderSpecialUse.Outbox)
        {
            yield return new ValidationResult(
                $"Folder alias '{this.Alias}' names the '{nameof(MailFolderSpecialUse.Outbox)}' role without a remote path, and no mail server advertises an outbox folder for discovery to find. Name the folder the role applies to in 'RemotePath'.",
                [nameof(this.SpecialUse)]);
        }

        foreach (var result in this.ValidateConfiguredValues(namesRemotePath))
        {
            yield return result;
        }

        foreach (var result in this.ValidateParticipation())
        {
            yield return result;
        }
    }

    /// <summary>Refuses a folder asked to do something an unmirrored folder cannot do.</summary>
    /// <remarks>
    /// Nothing here is a safety rule — the participation value already withdraws both from an unsynchronized folder, so
    /// the configuration would work. What it would not do is what it says, and a folder configured to embed mail nobody
    /// stores is an operator expecting a bill and a search result they will never get. The unset case is deliberately
    /// not refused: leaving a switch out is not asking for it.
    /// </remarks>
    private IEnumerable<ValidationResult> ValidateParticipation()
    {
        if (this.Synchronize is not false)
        {
            yield break;
        }

        if (this.GenerateEmbeddings is true)
        {
            yield return new ValidationResult(
                $"Folder alias '{this.Alias}' asks for embeddings while 'Synchronize' is false, and an unsynchronized folder stores no content to embed.",
                [nameof(this.GenerateEmbeddings)]);
        }

        if (this.VisibleToTools is true)
        {
            yield return new ValidationResult(
                $"Folder alias '{this.Alias}' asks to be visible to tools while 'Synchronize' is false, and an unsynchronized folder stores nothing a tool could read.",
                [nameof(this.VisibleToTools)]);
        }
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => this.ValidateForSynchronization();

    /// <summary>Reads a configured role name, accepting only the declared names themselves.</summary>
    /// <param name="configuredRole">The text an operator wrote, or <see langword="null" /> when they wrote none.</param>
    /// <param name="specialUse">The role when the text names one; otherwise the default.</param>
    /// <returns><see langword="true" /> when the text names a supported role.</returns>
    /// <remarks>
    /// Matched against the declared names rather than parsed, for the reason
    /// <see cref="MailFolderReference.TryCreate" /> gives at length: <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)" />
    /// accepts a number and a comma-separated list, the second of which combines its members by bitwise OR and so turns
    /// two roles an operator wrote into a third they did not. The two readings have to agree on what counts as a role,
    /// which is also why this is internal: the rule section reads the same key straight from configuration, before
    /// anything is bound, and a rule set startup accepted would otherwise be refused by the reload that read the same
    /// text through the other parser.
    /// </remarks>
    internal static bool TryParseSpecialUse(string? configuredRole, out MailFolderSpecialUse specialUse)
    {
        specialUse = default;

        return !string.IsNullOrWhiteSpace(configuredRole)
            && Enum.GetNames<MailFolderSpecialUse>()
                .Any(declared => string.Equals(declared, configuredRole.Trim(), StringComparison.OrdinalIgnoreCase))
            && Enum.TryParse(configuredRole.Trim(), ignoreCase: true, out specialUse);
    }

    /// <summary>Re-checks the alias and path against the domain rules, so an unusable value fails startup rather than the first run.</summary>
    private IEnumerable<ValidationResult> ValidateConfiguredValues(bool namesRemotePath)
    {
        if (!string.IsNullOrWhiteSpace(this.Alias) && !TryCreate(() => _ = MailFolderAlias.Create(this.Alias), out var aliasFailure))
        {
            yield return new ValidationResult(
                $"Folder alias '{this.Alias}' is not usable: {aliasFailure}",
                [nameof(this.Alias)]);
        }

        if (namesRemotePath && !TryCreate(() => _ = RemoteFolderPath.Create(this.RemotePath!), out var pathFailure))
        {
            yield return new ValidationResult(
                $"Folder alias '{this.Alias}' names a remote path that is not usable: {pathFailure}",
                [nameof(this.RemotePath)]);
        }
    }

    /// <summary>Turns a domain value object's rejection into a startup message, which is the one place it is expected.</summary>
    private static bool TryCreate(Action create, out string failure)
    {
        try
        {
            create();
            failure = string.Empty;

            return true;
        }
        catch (ArgumentException exception)
        {
            failure = exception.Message;

            return false;
        }
    }
}
