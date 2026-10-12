// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Host.Configuration.Policies;

/// <summary>Judges a change to a governed record's own document against the policies that govern the record.</summary>
/// <remarks>
/// <para>
/// A property is held against a writer in one of three ways: a policy forces it, which binds everybody; the editing
/// restriction withholds it, which binds the person the record is about and nobody administering them; or it is one
/// only an administrator writes, which no policy makes the person's. A change to a property held against whoever is
/// writing is refused in a sentence naming the property and whether the organization or the deployment holds it.
/// </para>
/// <para>
/// A held property that is carried through passes, and leaves the record's own value exactly as it was. Carried
/// through means stated as a read showed it: unchanged from the record's own value, or as the effective value a read
/// of the resolved record answers. The second has to pass because a caller that read the effective record and sends
/// it back whole is changing nothing — and it must not overwrite the own value on the way, or lifting a force would
/// restore the force's value under the person's name.
/// </para>
/// </remarks>
internal static class GovernedRecordChange
{
    /// <summary>Judges a candidate document against the one it would replace.</summary>
    /// <param name="section">The kind of record.</param>
    /// <param name="layers">What the policies governing the record say about its kind.</param>
    /// <param name="standingJson">The record's own document as it stands.</param>
    /// <param name="candidateJson">The document it would hold.</param>
    /// <param name="byThePerson">Whether the person the record is about is writing it, rather than somebody administering them.</param>
    /// <returns>The document to commit, with every held property the candidate carried through left as the record states it, and one sentence per held property the candidate would have changed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static GovernedRecordChangeJudgement Judge(
        SettingsPolicySection section,
        SettingsPolicyLayers layers,
        string standingJson,
        string candidateJson,
        bool byThePerson)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(standingJson);
        ArgumentNullException.ThrowIfNull(candidateJson);

        // A candidate that is no document is the binder's to refuse, in its own words.
        if (GovernedRecordResolution.ReadDocument(candidateJson) is not { } candidate)
        {
            return new GovernedRecordChangeJudgement(candidateJson, []);
        }

        var standing = GovernedRecordResolution.ReadDocument(standingJson) ?? [];
        var refusals = new List<string>();
        var carriedThrough = false;

        // A column beside the record is no key of its document, so no candidate document can move it.
        var documented = GovernedRecordResolution.Resolve(section, layers, standingJson).Properties
            .Where(resolved => resolved.Property.Accessors.Count > 0);

        foreach (var resolved in documented)
        {
            var before = SettingsDocumentTree.Find(standing, resolved.Property.Path);
            var after = SettingsDocumentTree.Find(candidate, resolved.Property.Path);

            if (SettingsDocumentTree.StateTheSame(before, after) || resolved.FindRefusal(byThePerson) is not { } refusal)
            {
                continue;
            }

            if (SettingsDocumentTree.StateTheSame(after, resolved.EffectiveValue))
            {
                SettingsDocumentTree.Replace(candidate, resolved.Property.Path, before);
                carriedThrough = true;
            }
            else
            {
                refusals.Add(refusal);
            }
        }

        return new GovernedRecordChangeJudgement(carriedThrough ? candidate.ToJsonString() : candidateJson, refusals);
    }
}
