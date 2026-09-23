// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Xunit;

// A scenario spends its time waiting on a provider rather than on a processor, and a test waiting on one still holds the
// slot xUnit started it in. The slots default to the processor count, four on a hosted runner, which left most scenario
// classes queued behind four that were only waiting. The level is therefore set by what may wait at once rather than by
// the machine: thirty-two is more than the scenario classes there are, and a class's own tests run one after another, so
// every class starts at once.
[assembly: CollectionBehavior(MaxParallelThreads = 32)]
