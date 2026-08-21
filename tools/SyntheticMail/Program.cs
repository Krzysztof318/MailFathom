// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail;

// A development tool and nothing else. It is built from source, ships in no artifact, is reachable from no project
// under backend/src/, and is not a command of mfctl: a command that fabricates mail and submits it under a stored credential
// is not an operator capability, and putting it in the published binary would invite it into a production mailbox.
//
// The work is in SyntheticMailRunner rather than here, because top-level statements cannot be called and a failure
// path nothing can exercise is one that drifts.
return await SyntheticMailRunner.RunAsync(SyntheticMailContext.ForTerminal(), args);
