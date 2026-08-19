// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Governance;

/// <summary>States why this deployment holds no capability to send as an account at all.</summary>
/// <remarks>
/// Both are the operator's posture rather than anything a caller did, and neither is reached by rewriting a request.
/// They are kept apart because the acts that resolve them differ: one is a switch on an account, and the other is the
/// posture the whole installation was started in.
/// </remarks>
public enum OutgoingSendRefusalReason
{
    /// <summary>Nobody has turned sending on for this account.</summary>
    /// <remarks>
    /// It is the default of every account of every deployment, so an installation upgrading into a release that can
    /// send does not thereby become able to. Turning it on is an act an operator performs per account, since an owner
    /// may want one identity able to write and another purely archival.
    /// </remarks>
    AccountNotEnabled = 0,

    /// <summary>The deployment is running read-only, in which nothing it holds may send.</summary>
    /// <remarks>
    /// It is read before the account's own switch, because a read-only installation is one whose operator has said the
    /// instance may not act outward at all — an answer no per-account setting is allowed to argue with, and one that
    /// would be worth nothing if a per-account switch could.
    /// </remarks>
    DeploymentIsReadOnly = 1,
}
