// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>Names one folder of an account, either by the alias configuration gave it or by the role it plays.</summary>
/// <remarks>
/// <para>
/// This is what a caller writes wherever a folder is named — a rule's destination, a folder filter a tool argument
/// carries — so that naming the junk folder does not oblige every deployment to agree on what it called that folder.
/// Which mapping a reference means is an account's question rather than this type's, so nothing here resolves one.
/// </para>
/// <para>
/// The written form is the alias itself, or the role behind the <c>role:</c> prefix — the same
/// <c>&lt;scheme&gt;:&lt;target&gt;</c> shape a secret reference is written in. The prefix is what keeps the two
/// unambiguous: an alias may legitimately read <c>Junk</c>, and without a marker a deployment that renamed its alias
/// would silently start meaning the role instead.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and names no folder at all. <see cref="IsSpecified" />
/// reports that, and every factory here produces a specified value, so the default can only arrive from code that
/// declared a field and never assigned it.
/// </para>
/// </remarks>
public readonly record struct MailFolderReference
{
    /// <summary>The scheme that marks the text after it as a role rather than as an alias.</summary>
    public const string RoleScheme = "role:";

    private MailFolderReference(MailFolderAlias? alias, MailFolderSpecialUse? role)
    {
        this.Alias = alias;
        this.Role = role;
    }

    /// <summary>Gets the alias this reference names, and <see langword="null" /> when it names a role instead.</summary>
    public MailFolderAlias? Alias { get; }

    /// <summary>Gets the role this reference names, and <see langword="null" /> when it names an alias instead.</summary>
    public MailFolderSpecialUse? Role { get; }

    /// <summary>Gets whether this value names a folder rather than being the unusable struct default.</summary>
    public bool IsSpecified => this.Alias is not null || this.Role is not null;

    /// <summary>Names the folder configuration gave that alias.</summary>
    /// <param name="alias">The operator-facing folder name.</param>
    /// <returns>The reference.</returns>
    public static MailFolderReference ToAlias(MailFolderAlias alias) => new(alias, role: null);

    /// <summary>Names whichever folder of the account plays that role.</summary>
    /// <param name="role">The role the folder plays.</param>
    /// <returns>The reference.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="role" /> is not a defined role.</exception>
    public static MailFolderReference ToRole(MailFolderSpecialUse role) => Enum.IsDefined(role)
        ? new MailFolderReference(alias: null, role)
        : throw new ArgumentOutOfRangeException(
            nameof(role),
            role,
            "A folder reference cannot name a special-use role that does not exist.");

    /// <summary>Reads the text a caller named a folder with.</summary>
    /// <param name="value">The alias, or a role written as <c>role:&lt;name&gt;</c>.</param>
    /// <returns>The reference the text names.</returns>
    /// <exception cref="ArgumentException">Thrown when the text is blank, carries a control character, or names a role that does not exist.</exception>
    public static MailFolderReference Create(string value) => TryCreate(value, out var reference)
        ? reference
        : throw new ArgumentException(
            $"'{DescribeRefusedText(value)}' names neither a folder alias nor one of the roles {string.Join(", ", Enum.GetNames<MailFolderSpecialUse>())}.",
            nameof(value));

    /// <summary>Reads the text a caller named a folder with, without raising on text that names none.</summary>
    /// <param name="value">The alias, or a role written as <c>role:&lt;name&gt;</c>.</param>
    /// <param name="reference">The reference when the text names one; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the text names a folder.</returns>
    /// <remarks>
    /// The role name is matched against the declared names rather than parsed, which refuses two spellings that
    /// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)" /> accepts and neither of which any operator meant.
    /// <c>role:4</c> would bind a typo onto whichever member currently carries that number. <c>role:Archive,Drafts</c>
    /// is worse: a comma-separated list is combined by bitwise OR whether or not the enumeration carries
    /// <see cref="FlagsAttribute" />, and these values are dense, so <c>Archive|Drafts</c> is <c>Sent</c> — a real role
    /// nobody wrote, which <see cref="Enum.IsDefined{TEnum}(TEnum)" /> then confirms. Both arrive here from a tool
    /// argument as readily as from configuration.
    /// </remarks>
    public static bool TryCreate(string? value, out MailFolderReference reference)
    {
        reference = default;

        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            return false;
        }

        var trimmed = value.Trim();

        if (!trimmed.StartsWith(RoleScheme, StringComparison.OrdinalIgnoreCase))
        {
            reference = ToAlias(MailFolderAlias.Create(trimmed));

            return true;
        }

        var roleName = trimmed[RoleScheme.Length..].Trim();

        if (!Enum.GetNames<MailFolderSpecialUse>()
            .Any(declared => string.Equals(declared, roleName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        reference = ToRole(Enum.Parse<MailFolderSpecialUse>(roleName, ignoreCase: true));

        return true;
    }

    /// <summary>Returns the reference in the form it is written in, so a message names it back the way a caller wrote it.</summary>
    /// <returns>The alias, the prefixed role, or a marker when the value is the struct default.</returns>
    public override string ToString() => this switch
    {
        { Alias: { } alias } => alias.Value,
        { Role: { } role } => $"{RoleScheme}{role}",
        _ => "(unspecified)",
    };

    /// <summary>Describes refused text without carrying a control character into a message that will be logged.</summary>
    private static string DescribeRefusedText(string? value) =>
        value is null || value.Any(char.IsControl) ? string.Empty : value;
}
