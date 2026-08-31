// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

/// <reference types="vite/client" />

/**
 * The version MailFathom declares, substituted into the bundle by `vite.config.ts` from `Version.props`. It is
 * declared rather than imported so that no source file and no manifest carries the number itself.
 */
declare const __MAILFATHOM_VERSION__: string;

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
