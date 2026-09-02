// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Contacts.Collection;

/// <summary>Reports what collection concluded, without reporting whom it concluded it about.</summary>
/// <remarks>
/// Collection is the one part of this system that writes personal data about third parties on its own initiative, so
/// what it did has to be visible or an owner could not tell a book that is filling from one that is not. The outcome is
/// MailFathom's own closed set and carries no address, no name, and no message identity, which is what lets the
/// instrument exist at all.
/// </remarks>
public interface IContactCollectionTelemetry
{
    /// <summary>Records what collection concluded about one address.</summary>
    /// <param name="outcome">The conclusion.</param>
    void RecordOutcome(ContactCollectionOutcome outcome);
}
