// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

/// <reference types="vite/client" />

/**
 * The version MailFathom declares, substituted into the bundle by `vite.config.ts` from `Version.props`. It is
 * declared rather than imported so that no source file and no manifest carries the number itself.
 */
declare const __MAILFATHOM_VERSION__: string;

interface Window {
    /**
     * The Tauri API the desktop shell injects, declared as the one part of it this application reaches.
     *
     * It is present only where a shell is hosting the page, which is what `credentialStore` reads to decide where the
     * credential is kept — a question about whether a shell is there rather than about which platform this is. The
     * shell turns it on through `withGlobalTauri` in `tauri.conf.json`, so the binding is Tauri's own rather than a
     * package this workspace pins and a web bundle then carries for a head it is not.
     */
    readonly __TAURI__?: {
        readonly core: {
            invoke: (command: string, argument?: Readonly<Record<string, unknown>>) => Promise<unknown>;
        };

        /**
         * How the shell says something happened outside the page, declared as narrowly as the invoke above it.
         *
         * One event is subscribed to today — a system notification somebody acted on, which
         * `shellOperations/systemNotifier.ts` answers — and it carries nothing, so the handler takes nothing. The
         * promise answers with what stops listening.
         */
        readonly event: {
            listen: (event: string, handler: () => void) => Promise<() => void>;
        };
    };
}

interface ImportMetaEnv {
    /**
     * Where the service serves its client surface, as an origin with no trailing separator, or absent when nothing
     * stated one.
     *
     * The Aspire app host writes it into the development server's environment so a local run reaches the service on
     * the port it happened to take. It is optional because every other way of serving this application states nothing:
     * a bundle served from the deployment's own container image is fetched from the service it calls, so the origin it
     * was loaded from is the answer, and a page opened from a development server without an orchestration behind it
     * has no service to name.
     */
    readonly VITE_MAILFATHOM_SERVICE_ADDRESS?: string;
}
