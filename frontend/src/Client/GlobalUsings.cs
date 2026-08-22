// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// Uno.Sdk already declares the WinUI, Uno.Extensions, and hosting namespaces globally, so only what it leaves out
// belongs here.
global using MailFathom.Client.Presentation;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;

// The MVUX generator writes one bindable type per model. Level 3 is the generation mode that produces the property
// accessors and command sources a XAML binding reaches, which is what lets a page bind to a model without a view model
// written by hand.
[assembly: Uno.Extensions.Reactive.Config.BindableGenerationTool(3)]
