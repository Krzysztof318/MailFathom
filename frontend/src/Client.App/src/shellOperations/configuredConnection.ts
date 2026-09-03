// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// What a deployment said about the connection before anybody typed anything: where its service is, and whether an
// unsecured connection to it is permitted. Somebody handed a packaged client by their organization should be asked for
// a password rather than for an address they would have to be told over the telephone, and a bundle built once and
// installed on many machines cannot answer that at build time — which is why the two values are read at start.
//
// Three places state them, and each is one an operator already configures the rest of a deployment from: the arguments
// the application was started with, the environment it was started in, and a file beside its own configuration. The
// order between them is the service's own, in `docs/operations/configuration-sources.md`, and it is repeated here
// rather than invented: a command line beats an environment, an environment beats a file. What the build wrote into
// the bundle sits beneath all three, and `deployment/adoptedDeployment.ts` is where it is reached.
//
// This module is a shell operation like `linkOpener.ts` beside it: only a shell has arguments, an environment, and a
// path to a configuration file, so a web head resolves nothing here and is asked for the address as before. Nothing
// underneath asks which head it is running on — what a screen receives is two values and where neither came from.

/** Where a setting was stated, highest precedence first. */
export const configurationSources = ['commandLine', 'environment', 'configurationFile'] as const;

export type ConfigurationSource = (typeof configurationSources)[number];

/**
 * What configuration stated about the connection, as it was written rather than as anything usable.
 *
 * Both are text because both arrive as text and neither is this module's to judge: an address that is not an address
 * and a permission that is not a permission are refusals a person is shown, so they have to survive being read.
 */
export interface ConfiguredConnection {
    readonly serviceAddress: string | null;
    readonly permitClearText: string | null;
}

/** What a deployment stated nothing about, which is every machine nobody configured and every web head. */
export const configuredNothing: ConfiguredConnection = { serviceAddress: null, permitClearText: null };

/**
 * Asks the shell what the three sources stated, and folds them into one answer by the precedence above.
 *
 * Each setting is folded on its own, because an operator putting the address in an installer's arguments and the
 * permission in a file is configuring one deployment rather than making a mistake.
 *
 * @returns What is in force, or {@link configuredNothing} where no shell is hosting the page or none of it answered.
 */
export async function configuredConnection(): Promise<ConfiguredConnection> {
    const shell = window.__TAURI__;

    if (shell === undefined) {
        return configuredNothing;
    }

    const answered: unknown = await shell.core.invoke('client_configuration').catch(() => null);

    return {
        serviceAddress: firstStated(answered, 'serviceAddress'),
        permitClearText: firstStated(answered, 'permitClearText'),
    };
}

/**
 * What the highest-precedence source that stated this setting said, or `null` where none of them did.
 *
 * Everything the shell answers is checked before it is read, for the reason any value crossing into the application is:
 * a shell that answered with a shape this client did not expect is a client that has to keep working, and the sign-in
 * screen it would otherwise render with a broken address is the screen a password is typed into. A blank value reads as
 * unset, because templating an installer's arguments routinely emits an empty string for a setting nobody set.
 */
function firstStated(answered: unknown, setting: keyof ConfiguredConnection): string | null {
    if (answered === null || typeof answered !== 'object') {
        return null;
    }

    const sources: Readonly<Record<string, unknown>> = answered as Readonly<Record<string, unknown>>;

    for (const source of configurationSources) {
        const stated: unknown = sources[source];

        if (stated === null || typeof stated !== 'object') {
            continue;
        }

        const value: unknown = (stated as Readonly<Record<string, unknown>>)[setting];

        if (typeof value === 'string' && value.trim().length > 0) {
            return value.trim();
        }
    }

    return null;
}
