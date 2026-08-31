import { useState } from "react";
import { aiFilters, folders, messages, threadComposer, threads } from "../data";
import { Card, Chip, Label, PaneHeader } from "../components/shell";

function Folders({ active, onSelect }: { active: string; onSelect: (folder: string) => void }) {
  return (
    <div className="flex flex-col gap-1">
      <div className="pb-2">
        <Label>Skrzynki</Label>
      </div>
      {folders.map((folder) => {
        const selected = folder.name === active;
        return (
          <button
            key={folder.name}
            type="button"
            onClick={() => onSelect(folder.name)}
            className={`flex cursor-pointer items-center justify-between rounded-lg px-3.5 py-2.5 text-sm ${
              selected ? "bg-accent-wash font-semibold text-accent-dark" : "hover:bg-rail"
            }`}
          >
            <span>{folder.name}</span>
            <span className={selected ? "" : "text-faint"}>{folder.count ?? ""}</span>
          </button>
        );
      })}

      <div className="mt-5 flex flex-col gap-2 border-t border-line pt-4">
        <Label>Filtry AI</Label>
        {aiFilters.map((filter) => (
          <button key={filter} type="button" className="cursor-pointer text-left text-sm text-ink-soft hover:text-ink">
            {filter}
          </button>
        ))}
      </div>
    </div>
  );
}

function MessageList({
  selectedId,
  showHints,
  onToggleHints,
  onSelect,
  onOpenFolders,
}: {
  selectedId?: string;
  showHints: boolean;
  onToggleHints: () => void;
  onSelect: (id: string) => void;
  onOpenFolders: () => void;
}) {
  return (
    <>
      <div className="flex shrink-0 items-center gap-2 border-b border-line px-4 py-3">
        <button
          type="button"
          onClick={onOpenFolders}
          aria-label="Skrzynki"
          className="cursor-pointer rounded-lg px-1 text-base text-muted lg:hidden"
        >
          ☰
        </button>
        <input
          placeholder="Szukaj lub opisz, czego potrzebujesz…"
          aria-label="Szukaj"
          className="min-w-0 flex-1 rounded-full border border-line bg-rail px-4 py-2 text-[13px] outline-none placeholder:text-faint focus:border-accent"
        />
      </div>

      <ul className="min-h-0 flex-1 overflow-y-auto">
        {messages.map((message) => {
          const selected = message.id === selectedId;
          return (
            <li key={message.id}>
              <button
                type="button"
                onClick={() => onSelect(message.id)}
                className={`flex w-full cursor-pointer flex-col gap-1.5 border-b px-4 py-3.5 text-left ${
                  selected ? "border-line bg-accent-wash" : "border-hairline hover:bg-sunken"
                }`}
              >
                <span className="flex items-baseline gap-2.5">
                  <span className="text-sm font-semibold">{message.from}</span>
                  {message.org && <span className="text-xs text-muted">{message.org}</span>}
                  <span className="ml-auto font-mono text-[11px] text-muted">{message.time}</span>
                </span>
                <span className={`text-[13px] ${selected ? "font-semibold" : ""}`}>{message.subject}</span>
                {showHints && message.hint && (
                  <span
                    className={`flex items-center gap-2 text-xs ${
                      message.hintStrong ? "text-accent-dark" : "text-muted"
                    }`}
                  >
                    <span
                      className={`rounded-[5px] px-1.5 py-0.5 font-mono text-[9px] ${
                        message.hintStrong ? "bg-accent-deep text-white" : "border border-chrome text-muted"
                      }`}
                    >
                      AI
                    </span>
                    {message.hint}
                  </span>
                )}
              </button>
            </li>
          );
        })}
      </ul>

      <button
        type="button"
        onClick={onToggleHints}
        className="shrink-0 cursor-pointer border-t border-line px-4 py-3 text-left text-xs text-muted hover:text-ink"
      >
        {showHints ? "Wyłącz podsumowania AI dla tego widoku" : "Włącz podsumowania AI dla tego widoku"}
      </button>
    </>
  );
}

function Reader({ threadId }: { threadId: string }) {
  const thread = threads[threadId];

  return (
    <>
      <div className="flex shrink-0 flex-col gap-2 border-b border-line px-4 py-3 lg:px-6 lg:py-4">
        <div className="hidden items-center gap-4 lg:flex">
          <h2 className="text-lg font-semibold">{thread.subject}</h2>
          <span className="ml-auto text-[13px] text-muted">Odpowiedz · Przekaż · Oflaguj</span>
        </div>
        <p className="text-[13px] text-muted">
          {thread.sender} · {thread.meta}
        </p>
      </div>

      <div className="flex shrink-0 flex-col gap-2.5 border-b border-line bg-sunken px-4 py-3.5 lg:px-6">
        <div className="flex items-center gap-3">
          <Label>ThreadState</Label>
          <span className="ml-auto hidden text-xs text-muted lg:inline">aktualizowany przy każdej nowej wiadomości</span>
        </div>
        <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-3">
          {thread.state.map((entry) => (
            <Card key={entry.label} className="flex flex-col gap-1.5 px-4 py-3">
              <span className="text-[11px] tracking-wide text-muted uppercase">{entry.label}</span>
              <span className="text-[13px] leading-snug">{entry.value}</span>
            </Card>
          ))}
        </div>
      </div>

      <div className="selectable flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto px-4 py-4 lg:px-6">
        <p className="text-sm leading-relaxed whitespace-pre-line text-ink-soft">{thread.intro}</p>

        {thread.highlight && (
          <p className="border-l-4 border-mark-edge bg-mark px-5 py-3.5 text-[15px] leading-relaxed text-pretty">
            {thread.highlight}
          </p>
        )}

        {thread.citation !== undefined && (
          <p className="flex items-center gap-3 font-mono text-xs text-muted">
            <span className="rounded-md bg-accent-wash px-2.5 py-1 text-accent-dark">cytowanie {thread.citation}</span>
            {thread.citationNote}
          </p>
        )}

        {thread.attachments.length > 0 && (
          <div className="flex flex-wrap gap-2.5">
            {thread.attachments.map((attachment) => (
              <div
                key={attachment.name}
                className="flex items-center gap-3 rounded-lg border border-line bg-sunken px-4 py-2.5"
              >
                <span className="font-mono text-[11px] text-muted">{attachment.kind}</span>
                <span className="text-[13px]">{attachment.name}</span>
                <span className="text-xs text-faint">{attachment.size}</span>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* The intent field stays reachable rather than scrolling away with the message. */}
      <div className="shrink-0 px-4 pb-4 lg:px-6">
        <div className="flex flex-col gap-2.5 rounded-xl border-2 border-accent bg-surface px-5 py-3.5">
          <div className="flex items-center gap-3">
            <span className="rounded-md bg-accent px-2 py-1 font-mono text-[10px] tracking-widest text-white">AI</span>
            <input
              placeholder={threadComposer.placeholder}
              aria-label="Zapytaj o wątek"
              className="min-w-0 flex-1 bg-transparent text-sm outline-none placeholder:text-faint"
            />
          </div>
          <div className="flex flex-wrap items-center gap-2 border-t border-hairline pt-2.5">
            <Chip tone="accent">{threadComposer.scope}</Chip>
            <Chip>{threadComposer.extra}</Chip>
            <span className="ml-auto hidden text-xs text-muted xl:inline">{threadComposer.note}</span>
          </div>
        </div>
      </div>
    </>
  );
}

export function Mail({
  threadId,
  isWide,
  onSelectThread,
  onBack,
}: {
  threadId?: string;
  isWide: boolean;
  onSelectThread: (id: string) => void;
  onBack: () => void;
}) {
  const [folder, setFolder] = useState("Odebrane");
  const [showHints, setShowHints] = useState(true);
  const [foldersOpen, setFoldersOpen] = useState(false);

  // On a phone the reading pane is a screen of its own, reached and left with back.
  if (threadId && !isWide) {
    return (
      <div className="flex min-h-0 flex-1 flex-col">
        <PaneHeader
          title={threads[threadId].subject}
          onBack={onBack}
          action={<span className="text-xs text-muted">Odpowiedz</span>}
        />
        <Reader threadId={threadId} />
      </div>
    );
  }

  return (
    <div className="relative flex min-h-0 flex-1">
      <div className="hidden w-[210px] shrink-0 flex-col border-r border-line bg-sunken px-4 py-5 lg:flex">
        <Folders active={folder} onSelect={setFolder} />
      </div>

      {foldersOpen && (
        <div className="absolute inset-0 z-10 flex lg:hidden">
          <div className="w-[230px] overflow-y-auto border-r border-line bg-sunken px-4 py-5">
            <Folders
              active={folder}
              onSelect={(next) => {
                setFolder(next);
                setFoldersOpen(false);
              }}
            />
          </div>
          <button
            type="button"
            aria-label="Zamknij skrzynki"
            onClick={() => setFoldersOpen(false)}
            className="flex-1 cursor-pointer bg-ink/20"
          />
        </div>
      )}

      <div
        className={`flex min-h-0 flex-col border-line lg:w-[360px] lg:shrink-0 lg:border-r ${
          threadId && isWide ? "hidden lg:flex" : "flex-1 lg:flex-none"
        }`}
      >
        <MessageList
          selectedId={threadId}
          showHints={showHints}
          onToggleHints={() => setShowHints((on) => !on)}
          onSelect={onSelectThread}
          onOpenFolders={() => setFoldersOpen(true)}
        />
      </div>

      {threadId ? (
        <div className="hidden min-h-0 flex-1 flex-col lg:flex">
          <Reader threadId={threadId} />
        </div>
      ) : (
        <div className="hidden flex-1 items-center justify-center text-sm text-faint lg:flex">
          Wybierz wiadomość, aby zobaczyć wątek
        </div>
      )}
    </div>
  );
}
