// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>What both stored-content levels had claimed when a measurement of them began.</summary>
/// <param name="DeploymentClaimMark">What the deployment level had claimed in total before the measurement started.</param>
/// <param name="OwnerClaimMark">What the owner's level had claimed before the measurement started.</param>
/// <remarks>
/// The two are carried together because one run takes both measurements and adopts both, and each level judges the
/// reading against its own mark: a payload another run claimed while the queries were in flight belongs on top of
/// whichever readings could not have described it.
/// </remarks>
public readonly record struct StoredContentMeasurementMark(long DeploymentClaimMark, long OwnerClaimMark);
