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
// `pnpm desktop:dev` merges a second value the same way: the port its development server is reached on, reserved here
// rather than written into `tauri.conf.json`. A fixed port is wrong on any machine running two of these at once — the
// second Vite finds the port taken and moves to the next free one, while the shell that started it goes on loading the
// first, so the window renders the other run's client instead of failing. Reserving the port in the one process that
// tells both halves about it removes that: `MAILFATHOM_DEV_PORT` is what the Vite configuration listens on and
// `build.devUrl` is what the shell loads, and neither is a number anybody typed.
//
// `pnpm desktop:dev` and `pnpm desktop:build` are what run this. Reaching the CLI through its own JavaScript API
// rather than through a shell keeps the command one process on every platform, which a quoted JSON argument in a
// package manifest would not be.

import { execFileSync } from 'node:child_process';
import { createServer } from 'node:net';
import { resolve } from 'node:path';
import { run } from '@tauri-apps/cli';

const desktopShell = import.meta.dirname;
const repositoryRoot = resolve(desktopShell, '../..');

// `MAILFATHOM_VERSION` is how a publication hands the number in rather than letting it be resolved a second time.
// `build-desktop-client.yml` is given a version its caller already resolved — a release's, or a nightly's identifier,
// which `Version.props` alone cannot produce — and a build that resolved its own would name the bundles something
// other than what the release attaching them says. Everywhere else the variable is unset and the declared version is
// read here, which is the only number a development build could mean.
//
// The fallback is invoked through `bash` rather than executed directly, so that a Windows machine building the desktop
// head resolves the script through Git Bash instead of asking the operating system to run a file it has no interpreter
// for.
const passedVersion = process.env['MAILFATHOM_VERSION']?.trim() ?? '';

const declaredVersion =
    passedVersion.length > 0
        ? passedVersion
        : execFileSync('bash', [resolve(repositoryRoot, 'scripts/read-declared-version.sh')], {
              encoding: 'utf8',
          }).trim();

// Asking the operating system for port zero is what makes the answer free rather than merely unused a moment ago.
// The port is released again before Vite binds it, which leaves a window another process could take it in; the Vite
// configuration therefore listens with `strictPort`, so that window ends in a refusal to start rather than in a
// development server quietly somewhere else.
async function reserveFreePort(): Promise<number> {
    const probe = createServer();
    try {
        await new Promise<void>((listening, failed) => {
            probe.once('error', failed);
            probe.listen(0, '127.0.0.1', listening);
        });

        const reserved = probe.address();
        if (reserved === null || typeof reserved === 'string') {
            throw new Error('The operating system reported no port for the development server.');
        }

        return reserved.port;
    } finally {
        probe.close();
    }
}

const forwardedArguments = process.argv.slice(2);
const configurationPatch: Record<string, unknown> = { version: declaredVersion };

if (forwardedArguments[0] === 'dev') {
    const developmentPort = String(await reserveFreePort());
    process.env['MAILFATHOM_DEV_PORT'] = developmentPort;
    configurationPatch['build'] = { devUrl: `http://localhost:${developmentPort}` };
}

// Everything after `--` is handed to the runner, which is Cargo. `--locked` is what makes a `Cargo.toml` that has
// moved away from `Cargo.lock` a refusal to build rather than a lock file quietly rewritten under the reviewed crate
// closure, and it is the counterpart of the `--frozen-lockfile` both verification gates install pnpm with.
await run([...forwardedArguments, '--config', JSON.stringify(configurationPatch), '--', '--locked'], 'run-tauri');
