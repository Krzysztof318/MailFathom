// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access;

/// <summary>Names the sole owner an act that carries none is for, where this deployment serves exactly one.</summary>
/// <remarks>
/// <para>
/// The deployment's own <c>MailSynchronization:Accounts</c> section names no owner, so its accounts belong to whichever
/// sole owner the deployment holds, and this is that owner. A deployment may instead declare its owners, and then it
/// serves as many as its file declares and there is no sole one to name: reading this refuses rather than answering,
/// because attributing an act to whichever owner a read happened to find is how one person is handed another person's
/// mail. What keeps that refusal off a running deployment is the startup gate, which will not serve a roster of several
/// owners while any surface that reads this is enabled.
/// </para>
/// <para>
/// It is what makes an admitted caller a caller acting for somebody. A credential is configured today and carries no
/// owner of its own, so every caller a mail-reading surface admits is acting for this one; when credentials become
/// records of their own, the owner comes off the credential and this port stops being what answers for a caller. An
/// administrator's acts are the deployment's rather than one person's and carry no owner either, so those resolve here
/// as well.
/// </para>
/// <para>
/// The answer is a value rather than a read, because it is settled once and consulted per request. Nothing here reaches
/// a database while a request is being served, and nothing about a request can change the answer.
/// </para>
/// </remarks>
public interface IDeploymentMailOwnerSource
{
    /// <summary>Gets the owner this deployment's configured mail accounts belong to.</summary>
    /// <exception cref="InvalidOperationException">Thrown when this deployment serves several owners, which leaves no sole owner for an act carrying none to be attributed to, and when the roster has not been established yet.</exception>
    MailOwnerId Owner { get; }
}
