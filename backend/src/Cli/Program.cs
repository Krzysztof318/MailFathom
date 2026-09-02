// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli;

// The administration tool is a composition root and nothing else. Every operation it performs is an HTTP request to a
// deployment's administrative endpoint: it never hosts the service, never reads its configuration, and never opens its
// database. That is what lets it be a single self-contained binary an operator runs on their own machine, whichever
// platform that is, against a deployment running somewhere else.
//
// The work is in CliRunner rather than here, because top-level statements cannot be called and a failure path nothing
// can exercise is one that drifts.
return await CliRunner.RunAsync(CliContext.ForTerminal(), args);
