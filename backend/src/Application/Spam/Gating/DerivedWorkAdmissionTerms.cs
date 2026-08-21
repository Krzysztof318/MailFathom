// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;

namespace MailFathom.Application.Spam.Gating;

/// <summary>The terms one moment's admissions are decided under, as a value a query can be narrowed by.</summary>
/// <remarks>
/// <para>
/// The reason this exists beside <see cref="DerivedWorkGate.Admit(DerivedWorkCandidate)" /> is the reason
/// <see cref="Folders.IMailFolderParticipationReader" /> answers in two shapes: a walk over stored mail narrows a table
/// and needs the whole decision as a value it can put into a predicate, while the arrival path holds one occurrence and
/// asks about that one. Both are built here, from one reading of the settings and one reading of the clock, so the two
/// cannot disagree about what a moment admits.
/// </para>
/// <para>
/// It is a snapshot rather than a live view, so one decision is never made against a settings reload half way through
/// itself. Which moment a walk's batches are decided at is the walk's own business: the wait only ever moves forward,
/// so a batch read later releases what an earlier one held and never the reverse.
/// </para>
/// </remarks>
/// <param name="IsApplied">
/// Whether the gate reaches anything at all. False with classification switched off, which is what makes a deployment
/// that classifies nothing behave exactly as it did before the gate existed.
/// </param>
/// <param name="JunkFolders">Every account's junk folder, whose mail is withheld with nothing having to score it.</param>
/// <param name="ClassifiedFolderAliases">
/// The folder aliases classification runs over. Mail outside them is admitted rather than left waiting, because nothing
/// is ever going to reach a verdict about it.
/// </param>
/// <param name="ReleasedWhenStoredBefore">
/// The instant a message must have been stored before to be released without a verdict. It is the moment the terms were
/// read, less the wait a verdict is allowed.
/// </param>
public sealed record DerivedWorkAdmissionTerms(
    bool IsApplied,
    IReadOnlyList<MailFolderIdentity> JunkFolders,
    IReadOnlyList<MailFolderAlias> ClassifiedFolderAliases,
    DateTimeOffset ReleasedWhenStoredBefore)
{
    /// <summary>Reports whether classification runs over one folder.</summary>
    /// <param name="folderAlias">MailFathom's own name for the folder.</param>
    /// <returns><see langword="true" /> when a verdict is expected for mail in that folder.</returns>
    public bool Classifies(MailFolderAlias folderAlias) => this.ClassifiedFolderAliases.Contains(folderAlias);
}
