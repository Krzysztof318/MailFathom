// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation;

/// <summary>
/// The model behind <see cref="MainPage"/>. The application is empty of features, so the only thing it has to say is
/// what it is: the product name and the version this build was stamped with.
/// </summary>
public partial record MainModel
{
    /// <summary>What this build of the client reports about itself.</summary>
    public IFeed<ClientBuild> Build => Feed.Async(_ => ValueTask.FromResult(ClientBuild.Current));
}
