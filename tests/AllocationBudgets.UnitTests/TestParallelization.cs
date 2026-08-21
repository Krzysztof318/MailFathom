// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Xunit;

// A budget is read from what the whole process allocated during the measured run, because an asynchronous path may
// resume on a thread other than the one it started on and a per-thread counter would then report a fraction of the
// truth. That reading is only usable while nothing else in this assembly is allocating, so the suite runs one test at
// a time. It costs seconds here and nowhere else: this project holds the budgets and nothing besides them.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
