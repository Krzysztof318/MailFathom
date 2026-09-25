// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Xunit.Sdk;
using Xunit.v3;

// Every case runs beside every other, whichever class declares it, so a class with many questions finishes in the time
// of its slowest question rather than in the sum of them. A case spends its time waiting on a provider rather than on a
// processor, and a case waiting on one still holds the slot it started in, so the level is set by what the providers may
// be asked at once rather than by the machine: sixty-four cases, each asking every declared model, with
// TransientProviderRetryChatClient asking again after a rate limit that goes past it. xUnit takes the limit for a whole
// assembly only, which is why the free tests live in Evaluations.UnitTests and run under the unit suites' own defaults.
[assembly: Parallelization(Mode = ParallelMode.All, MaxThreads = 64)]
