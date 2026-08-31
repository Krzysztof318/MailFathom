// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Configuration;
using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.OwnerSettings.Administration;

/// <summary>What adopting one owner would move out of this deployment's files and into their record.</summary>
/// <param name="Owner">The owner the adoption is for.</param>
/// <param name="DisplayName">The label the deployment tells them apart by.</param>
/// <param name="Version">The version their record stands at, which the adoption is composed over and refused against.</param>
/// <param name="Source">Where their mail accounts are read from today, which is what the adoption changes.</param>
/// <param name="ConfigurationPath">The colon-delimited section their declarations are written in, or <see langword="null" /> when no configuration source reaches them.</param>
/// <param name="MailAccounts">The mail accounts the adoption would materialize, named as an operator recognizes them.</param>
/// <param name="Classification">The classification posture the adoption would commit beside them, empty when the deployment states none.</param>
/// <param name="SensitiveContent">The scanning block the adoption would commit beside them, empty when their declaration states none.</param>
/// <remarks>
/// The section is the part an operator weighs, exactly as the deployment's own adoption names the file behind each
/// setting: it is what stops deciding this owner's mailboxes once the adoption commits, and it is where somebody would
/// otherwise go on editing them with no effect. An owner whose record is already their own previews nothing and is
/// reported as needing no adoption rather than as an error.
/// </remarks>
internal sealed record OwnerAdoptionPreview(
    MailOwnerId Owner,
    string DisplayName,
    long Version,
    MailOwnerAccountSource Source,
    string? ConfigurationPath,
    IReadOnlyList<OwnerAdoptableMailAccount> MailAccounts,
    IReadOnlyList<OwnerAdoptableRecordSetting> Classification,
    IReadOnlyList<OwnerAdoptableRecordSetting> SensitiveContent)
{
    /// <summary>Gets whether there is an adoption to perform at all.</summary>
    /// <remarks>False for an owner whose record is already their own, which is the repeat of an adoption that has run.</remarks>
    public bool HasSomethingToAdopt => this.Source != MailOwnerAccountSource.OwnerDocument;
}

/// <summary>One mail account an adoption would move into the owner's record.</summary>
/// <param name="AccountId">The identifier the account is declared under, which is what every later act on it names.</param>
/// <param name="DisplayName">The name the account is published under, which is what a person recognizes.</param>
/// <remarks>
/// Two fields and no more. What an operator confirms is which mailboxes stop being decided by the file, and every other
/// setting of an account — the server, the user name, the secret reference — is either uninteresting to that decision or
/// something a preview should not be repeating into a terminal.
/// </remarks>
internal sealed record OwnerAdoptableMailAccount(string AccountId, string DisplayName);

/// <summary>One setting an adoption would commit into the owner's record beside their mailboxes.</summary>
/// <param name="Path">The path the setting is written at in the record, rooted at its own block.</param>
/// <param name="Value">The value it takes, which is what the deployment's section states today.</param>
/// <remarks>
/// The value is here because the setting name alone does not say what an operator is agreeing to: filing named without
/// its value reads the same whether it is about to be switched on or off, two of these settings write to that owner's
/// mail server, and one of them decides whether their mail is scanned before it leaves this process. Nothing under
/// either section's scanner block reaches this type, so no daemon address, no analyzer address, and no credential is
/// repeated into a terminal by previewing an adoption.
/// </remarks>
internal sealed record OwnerAdoptableRecordSetting(string Path, string Value)
{
    /// <summary>Describes one posture change an adoption would commit.</summary>
    /// <param name="edit">The change as the adoption composed it.</param>
    /// <returns>The preview entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="edit" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="edit" /> removes the setting rather than stating a value for it.</exception>
    /// <remarks>An adoption states values and removes nothing, so a change carrying no value cannot arrive here and is refused rather than rendered as an empty setting.</remarks>
    internal static OwnerAdoptableRecordSetting For(ConfigurationEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);

        return new OwnerAdoptableRecordSetting(
            edit.Path,
            edit.Value ?? throw new ArgumentException("An adoption states a value for every setting it carries.", nameof(edit)));
    }
}
