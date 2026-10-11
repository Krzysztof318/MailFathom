// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Policies;

/// <summary>Marks a property of a governed record as belonging to a class other than the ordinary one.</summary>
/// <param name="propertyClass">The class the property, and everything beneath it where it is a block, belongs to.</param>
/// <remarks>
/// Stated on the property where the record's own type declares it, so the set of governable properties follows the
/// type and no list is kept beside it: a property nobody marked is an ordinary one, which is the right answer for
/// nearly every setting a later release adds. A secret block needs no mark, because it is recognized by the type it
/// binds as. Declared in this assembly rather than beside the policy's administration because a governed record's
/// settings are declared in two — the host's own options and the bindable shapes here — and a mark has to be
/// reachable from both.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingsPolicyClassAttribute(SettingsPolicyPropertyClass propertyClass) : Attribute
{
    /// <summary>Gets the class the marked property belongs to.</summary>
    public SettingsPolicyPropertyClass PropertyClass { get; } = propertyClass;
}
