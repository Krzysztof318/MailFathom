// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Which of the two things the mail accounts of a document being bound are: declarations somebody is writing, or ones already held.</summary>
/// <remarks>
/// <para>
/// Almost every rule is the same in both directions, which is why one binder serves them: a declaration that would be
/// refused as a candidate must not be accepted as a row. Two rules are the exception, and both for one reason: each
/// judges an account against something outside it that moved after the account was accepted, so refusing a held
/// account by it would refuse the start rather than the account.
/// </para>
/// <para>
/// A scanning block is that case. It is judged against the deployment's own section, and an operator switching a
/// scanner on deployment-wide or widening what stops outgoing mail — the tightening this whole feature exists to keep
/// available — turns every already-accepted account that asked for less into one that reads as a loosening. Refusing
/// those at the next start would refuse the start itself, for every user, over a change nobody can now undo from
/// inside: the surface an account would be rewritten through is behind the gate that is failing. So a stored
/// account's scanning block is composed rather than refused, which takes the stricter of the two exactly as it would
/// have.
/// </para>
/// <para>
/// The language an account must name is the other. Every account recorded before that property existed states none,
/// and the binding is strict, so no administrator could have written one in advance of the release that began asking
/// for it — a start refusing them would be one no <c>mfctl</c> could reach to correct, because the administrative
/// surface is behind the gate that is failing. So a held account stating no language reads as English, which is what
/// every unresolved read already answers, and states one the first time it is written.
/// </para>
/// <para>
/// Both rules are the accounts' rather than the record's, which is why a user saving their own record judges its
/// accounts as already held: that write carries no account block through, so a refusal over one would name something
/// the record being written does not contain.
/// </para>
/// </remarks>
internal enum UserRecordArrival
{
    /// <summary>Declarations being written, judged by every rule including what the deployment allows them to ask for.</summary>
    BeingWritten = 0,

    /// <summary>Declarations this deployment already holds, whose scanning block the composition takes the stricter of and whose unstated language reads as English.</summary>
    AlreadyHeld = 1,
}
