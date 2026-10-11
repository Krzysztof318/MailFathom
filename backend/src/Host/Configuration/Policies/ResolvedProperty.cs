// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Nodes;
using MailFathom.Infrastructure.Policies;

namespace MailFathom.Host.Configuration.Policies;

/// <summary>One property of a governed record as the policies governing it resolve it.</summary>
/// <param name="Property">The property.</param>
/// <param name="Source">Which step of the resolution order supplied its effective value.</param>
/// <param name="EffectiveValue">The value the record is served with, or <see langword="null" /> where nothing states one and the build takes none either.</param>
/// <param name="WithheldBy">The scope whose editing restriction does not leave the property to the person, or <see langword="null" /> where every policy does.</param>
/// <remarks>
/// A list and a secret block answer no effective value here where nothing states one, because the value the build
/// takes for either is not one text a reader could be shown.
/// </remarks>
internal sealed record ResolvedProperty(
    GovernableProperty Property,
    SettingsValueSource Source,
    JsonNode? EffectiveValue,
    SettingsPolicyScope? WithheldBy)
{
    /// <summary>Gets the scope whose policy forces the property, or <see langword="null" /> where none does.</summary>
    internal SettingsPolicyScope? ForcedBy => this.Source.ForcedBy;

    /// <summary>Gets the scope that holds the property against the person the record is about, or <see langword="null" /> where no policy does.</summary>
    /// <remarks>A force is named before a restriction, because lifting the restriction would still leave the force.</remarks>
    internal SettingsPolicyScope? HeldBy => this.ForcedBy ?? this.WithheldBy;

    /// <summary>Gets a value indicating whether an administrator editing the record may change the property.</summary>
    /// <remarks>A forced value binds everybody; the editing restriction binds the person alone.</remarks>
    internal bool IsAnAdministratorsToChange => this.ForcedBy is null;

    /// <summary>Gets a value indicating whether the person the record is about may change the property.</summary>
    internal bool IsThePersonsToChange =>
        this.HeldBy is null && this.Property.PropertyClass != SettingsPolicyPropertyClass.AdministratorOnly;

    /// <summary>Says why a change to the property is refused.</summary>
    /// <param name="byThePerson">Whether the person the record is about is the one changing it.</param>
    /// <returns>One sentence naming the property and who holds it, or <see langword="null" /> where the change may stand.</returns>
    internal string? FindRefusal(bool byThePerson)
    {
        if (this.ForcedBy is { } forcing)
        {
            return $"{this.Property.Path} is held by {Named(forcing)}, whose settings policy forces its value, so nothing written on the record changes it. Leave it as the record states it, or change the policy.";
        }

        if (!byThePerson)
        {
            return null;
        }

        if (this.Property.PropertyClass == SettingsPolicyPropertyClass.AdministratorOnly)
        {
            return $"{this.Property.Path} is decided by whoever administers this deployment and never by the person the record is about. Leave it as your record already states it.";
        }

        return this.WithheldBy is { } withholding
            ? $"{this.Property.Path} is held by {Named(withholding)}, whose settings policy does not leave it to you to change. Leave it as it stands, or ask whoever administers {Named(withholding)}."
            : null;
    }

    private static string Named(SettingsPolicyScope scope) =>
        scope == SettingsPolicyScope.Deployment ? "the deployment" : "the organization";
}
