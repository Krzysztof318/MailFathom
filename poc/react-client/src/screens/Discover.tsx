import { useState } from "react";
import { accounts, mailboxSummary, recentQuestions, savedViews, scopeSummary, syncSummary } from "../data";
import { Card, Chip, Label } from "../components/shell";

/** The intent field: one text box plus an explicit scope bar, never a mode switch. */
function IntentField({ onAsk }: { onAsk: (question: string) => void }) {
  const [question, setQuestion] = useState("");

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        onAsk(question);
      }}
      className="flex flex-col gap-3 rounded-[14px] border-2 border-accent bg-surface p-4 shadow-[0_2px_14px_rgba(20,22,26,0.06)]"
    >
      <div className="flex items-center gap-3">
        <span className="rounded-md bg-accent px-2 py-1 font-mono text-[10px] tracking-widest text-white">AI</span>
        <input
          value={question}
          onChange={(event) => setQuestion(event.target.value)}
          placeholder="O co chcesz zapytać swoją pocztę?"
          aria-label="Pole intencji"
          className="min-w-0 flex-1 bg-transparent text-base outline-none placeholder:text-faint"
        />
        <button type="submit" aria-label="Zapytaj" className="cursor-pointer font-mono text-sm text-muted">
          ⏎
        </button>
      </div>

      <div className="flex flex-wrap items-center gap-2 border-t border-hairline pt-3">
        <Chip>Wszystkie skrzynki ▾</Chip>
        <Chip>2019–2026 ▾</Chip>
        <Chip>Z załącznikami</Chip>
        <span className="ml-auto hidden text-xs text-muted xl:inline">{scopeSummary}</span>
      </div>
    </form>
  );
}

export function Discover({ onAsk }: { onAsk: (question: string) => void }) {
  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <header className="flex shrink-0 items-center justify-between border-b border-line px-4 py-3 lg:px-8">
        <h1 className="text-base font-semibold">Odkrywaj</h1>
        <div className="flex items-center gap-5 text-xs text-muted">
          <span className="flex items-center gap-2">
            <span className="size-2 rounded-full bg-fresh" />
            {syncSummary}
          </span>
          <span className="hidden sm:inline">{mailboxSummary}</span>
        </div>
      </header>

      <div className="flex min-h-0 flex-1 flex-col gap-8 overflow-y-auto p-4 lg:flex-row lg:gap-10 lg:p-8">
        <div className="flex max-w-[900px] flex-1 flex-col gap-7">
          <div className="flex flex-col gap-2">
            <h2 className="text-2xl font-semibold tracking-tight lg:text-3xl">Znajdź wiedzę w całej historii poczty</h2>
            <p className="text-sm text-muted">Pytanie, filtr albo polecenie — jedno pole, bez wybierania trybu.</p>
          </div>

          <IntentField onAsk={onAsk} />

          <section className="flex flex-col gap-3">
            <Label>Ostatnie pytania</Label>
            {recentQuestions.map((question) => (
              <button
                key={question.id}
                type="button"
                onClick={() => onAsk(question.text)}
                className="flex cursor-pointer items-center gap-4 rounded-[10px] border border-line bg-surface px-5 py-4 text-left transition-colors hover:border-accent"
              >
                <span className="min-w-0 flex-1 text-sm">{question.text}</span>
                <span className="hidden text-xs text-muted sm:inline">{question.blocks}</span>
                <span className="font-mono text-xs text-faint">{question.when}</span>
              </button>
            ))}
          </section>
        </div>

        <aside className="flex flex-col gap-3 lg:w-[300px] lg:shrink-0">
          <Label>Konta i świeżość</Label>
          <Card className="flex flex-col gap-3 px-5 py-4">
            {accounts.map((account) => (
              <div key={account.address} className="flex items-baseline justify-between gap-3">
                <span className="min-w-0 truncate text-sm">{account.address}</span>
                <span className={`font-mono text-xs ${account.stale ? "text-stale" : "text-fresh"}`}>
                  {account.freshness}
                </span>
              </div>
            ))}
            <p className="border-t border-hairline pt-3 text-xs leading-relaxed text-muted">
              Wynik zawsze pokazuje najstarszą synchronizację w zakresie.
            </p>
          </Card>

          <div className="flex flex-col gap-2 rounded-[10px] border border-line bg-rail px-5 py-4">
            <h3 className="text-sm font-semibold">Zapisane widoki</h3>
            <ul className="flex flex-col gap-1 text-sm text-ink-soft">
              {savedViews.map((view) => (
                <li key={view}>{view}</li>
              ))}
            </ul>
          </div>
        </aside>
      </div>
    </div>
  );
}
