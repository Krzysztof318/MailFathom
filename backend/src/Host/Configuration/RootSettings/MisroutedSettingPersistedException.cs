// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Host.Configuration.RootSettings;

/// <summary>Indicates that the persisted root document carries a setting MailFathom persists in another store.</summary>
/// <remarks>
/// The message names the misrouted settings and never their values, for the reason
/// <see cref="BootstrapOnlySettingPersistedException" /> gives: a key is MailFathom's own name for a setting, and a
/// reader repairing the document is already looking at what sits behind it.
/// </remarks>
public sealed class MisroutedSettingPersistedException : MailFathomException
{
    /// <summary>Initializes a new failure naming the persisted settings that belong in another store.</summary>
    /// <param name="operatorSafeMessage">A message naming the settings and the correction, and no configured value.</param>
    public MisroutedSettingPersistedException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.MisroutedSettingPersisted;
}
