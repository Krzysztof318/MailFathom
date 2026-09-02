// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Infrastructure.Mail.MailKit.Delivery;

/// <summary>Stops a submission whose envelope the server accepted nobody for.</summary>
/// <remarks>
/// <para>
/// The mail library refuses a submission with no accepted recipient by calling a hook that is expected to raise, and
/// this is what that hook raises. It exists rather than a library exception because the answer it stands for is already
/// written down: every reply is on the envelope ledger, and the attempt reads what the refusal was from there rather
/// than from a message assembled here.
/// </para>
/// <para>
/// It is a signal between the client and the connection that issued the submission, and the connection catches every
/// one it raises, so it reaches no boundary and carries no error code — which is why it is internal.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "It is a control-flow signal from the submission client to the connection that issued the submission, caught there and never observed by a caller, so it publishes no failure identity of its own.")]
internal sealed class SmtpNoRecipientsAcceptedException : Exception
{
    /// <summary>Initializes the signal that a submission's envelope settled with no address accepted.</summary>
    internal SmtpNoRecipientsAcceptedException()
        : base("The submission server accepted none of the offered recipients, so the message was not transmitted.")
    {
    }
}
