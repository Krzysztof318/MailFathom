import { useCallback, useEffect, useState } from "react";

export type View =
  | { kind: "discover" }
  /** `cite` open means the evidence inspector has replaced the evidence list. */
  | { kind: "result"; cite?: number }
  | { kind: "mail"; threadId?: string }
  | { kind: "cases" };

export type Section = "discover" | "mail" | "cases";

export const sectionOf = (view: View): Section => (view.kind === "result" ? "discover" : view.kind);

/**
 * `#/result/5` or `#/mail/aneks` — a deep link into one screen. Read once at
 * start-up so a screen can be opened directly; afterwards the History API
 * carries the state and the hash is left alone.
 */
function viewFromHash(hash: string): View | undefined {
  const [kind, argument] = hash.replace(/^#\/?/, "").split("/");

  if (kind === "result") return { kind: "result", cite: argument ? Number(argument) : undefined };
  if (kind === "mail") return { kind: "mail", threadId: argument || undefined };
  if (kind === "cases") return { kind: "cases" };
  return undefined;
}

const initial: View = viewFromHash(window.location.hash) ?? { kind: "discover" };

/**
 * Navigation over the History API rather than a router: the back gesture on
 * Android and the desktop window's back binding both arrive as `popstate`, so
 * one listener gives the PoC application-like back with no dependency.
 */
export function useNavigation() {
  const [view, setView] = useState<View>(() => (window.history.state?.view as View) ?? initial);

  useEffect(() => {
    if (!window.history.state?.view) {
      window.history.replaceState({ view: initial }, "");
    }

    const onPopState = (event: PopStateEvent) => setView((event.state?.view as View) ?? initial);

    window.addEventListener("popstate", onPopState);
    return () => window.removeEventListener("popstate", onPopState);
  }, []);

  const push = useCallback((next: View) => {
    window.history.pushState({ view: next }, "");
    setView(next);
  }, []);

  const replace = useCallback((next: View) => {
    window.history.replaceState({ view: next }, "");
    setView(next);
  }, []);

  const back = useCallback(() => window.history.back(), []);

  return { view, push, replace, back };
}

/** Tracks a media query so a pane can be a column on desktop and a pushed screen on a phone. */
export function useMediaQuery(query: string) {
  const [matches, setMatches] = useState(() => window.matchMedia(query).matches);

  useEffect(() => {
    const list = window.matchMedia(query);
    const onChange = () => setMatches(list.matches);

    setMatches(list.matches);
    list.addEventListener("change", onChange);
    return () => list.removeEventListener("change", onChange);
  }, [query]);

  return matches;
}

export const useIsWide = () => useMediaQuery("(min-width: 1024px)");
