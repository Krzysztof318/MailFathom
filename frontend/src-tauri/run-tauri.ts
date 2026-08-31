// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The Tauri CLI, run with the declared version merged into the configuration.
//
// `<VersionPrefix>` in `Version.props` is the only application version number in this repository, so `tauri.conf.json`
// carries none and neither does `Cargo.toml`. Tauri's `--config` takes a JSON Merge Patch applied over the resolved
// configuration, which is the same shape every other artifact here gets its number by: the image reads it into a
// build argument and the chart into `--app-version`, and nothing commits a second copy of the number to disagree with
// the first.
//
// `pnpm desktop:dev` and `pnpm desktop:build` are what run this. Reaching the CLI through its own JavaScript API
// rather than through a shell keeps the command one process on every platform, which a quoted JSON argument in a
// package manifest would not be.

import { execFileSync } from 'node:child_process';
import { resolve } from 'node:path';
import { run } from '@tauri-apps/cli';

const desktopShell = import.meta.dirname;
const repositoryRoot = resolve(desktopShell, '../..');

// Invoked through `bash` rather than executed directly, so that a Windows machine building the desktop head resolves
// the script through Git Bash instead of asking the operating system to run a file it has no interpreter for.
const declaredVersion = execFileSync('bash', [resolve(repositoryRoot, 'scripts/read-declared-version.sh')], {
    encoding: 'utf8',
}).trim();

const forwardedArguments = process.argv.slice(2);

await run([...forwardedArguments, '--config', JSON.stringify({ version: declaredVersion })], 'run-tauri');
