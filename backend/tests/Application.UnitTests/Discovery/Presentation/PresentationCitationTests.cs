// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Presentation;

/// <summary>Covers what a declared source has to carry before a plan can name it.</summary>
public sealed class PresentationCitationTests
{
    /// <summary>A citation resolving to nothing is a source a reader is invited to follow and cannot.</summary>
    [Fact]
    public void Constructor_NoTarget_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new PresentationCitation(
            PresentationPlanExample.FirstCitation,
            target: null!,
            PresentationPlanExample.Text("Revised figures")));
    }

    /// <summary>A label is what the source reads as before it is followed, so a citation without one presents nothing.</summary>
    [Fact]
    public void Constructor_ALabelThatIsTheStructDefault_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new PresentationCitation(
            PresentationPlanExample.FirstCitation,
            new EmailCitationTarget(StoredEmailId.Create(new Guid("11111111-1111-1111-1111-111111111111"))),
            label: default));
    }
}
