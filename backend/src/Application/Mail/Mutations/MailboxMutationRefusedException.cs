// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Indicates that a mail server was reached, answered, and will answer the same way every later time.</summary>
/// <remarks>
/// <para>
/// It exists so the two callers that act on that fact name one type instead of each keeping a list of the concrete
/// refusals. The performer abandons a mutation raising one of these on its first occurrence rather than spending the
/// attempt bound, and a convergence pass counts it as given up on rather than as failed — and those two decisions would
/// silently disagree the moment a third refusal was added to one list and not the other.
/// </para>
/// <para>
/// What every subclass asserts is the same thing, and it is the opposite of what
/// <see cref="Synchronization.Sessions.MailboxUnavailableException" /> asserts: the server did not fail to answer, it
/// answered, and the answer is a decision an operator has to change something to alter. A failure that might clear on
/// its own is not one of these, however certain it looks.
/// </para>
/// <para>
/// It carries no <see cref="MailFathomErrorCode" /> of its own, because a code names one failure a boundary reports and
/// each subclass has one. Being abstract, it is never raised and never caught in place of knowing which refusal
/// happened where that matters.
/// </para>
/// </remarks>
public abstract class MailboxMutationRefusedException : MailFathomException
{
    /// <summary>Initializes a new refusal with a message safe to surface.</summary>
    /// <param name="operatorSafeMessage">A message free of credentials, hosts, remote paths, message content, and personal data.</param>
    protected MailboxMutationRefusedException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    /// <summary>Initializes a new refusal with a message safe to surface and the failure that revealed it.</summary>
    /// <param name="operatorSafeMessage">A message free of credentials, hosts, remote paths, message content, and personal data.</param>
    /// <param name="innerException">The failure this refusal was raised for.</param>
    protected MailboxMutationRefusedException(string operatorSafeMessage, Exception innerException)
        : base(operatorSafeMessage, innerException)
    {
    }
}
