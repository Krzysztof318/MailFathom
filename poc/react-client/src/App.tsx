import { Discover } from "./screens/Discover";
import { Mail } from "./screens/Mail";
import { Result } from "./screens/Result";
import { Rail, TabBar } from "./components/shell";
import { evidenceList, evidenceDocument } from "./data";
import { sectionOf, useIsWide, useNavigation, type Section } from "./navigation";

/** The one thread a citation resolves to, so "Otwórz w poczcie" lands somewhere real. */
const sourceThreadId = evidenceList.items.find((item) => item.cite === evidenceDocument.cite)?.threadId;

function Cases() {
  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-2 p-8 text-center">
      <h1 className="text-xl font-semibold">Sprawy</h1>
      <p className="max-w-sm text-sm text-muted">
        Trwała pamięć pracy — źródła, ustalenia, timeline i szkice. Poza zakresem tego demo.
      </p>
    </div>
  );
}

export default function App() {
  const { view, push, replace, back } = useNavigation();
  const isWide = useIsWide();

  const goToSection = (section: Section) => {
    if (section === sectionOf(view)) return;
    push(section === "discover" ? { kind: "discover" } : { kind: section });
  };

  return (
    <div className="flex h-full bg-canvas">
      <Rail active={sectionOf(view)} onSelect={goToSection} />

      <div className="flex min-w-0 flex-1 flex-col">
        <main className="flex min-h-0 flex-1 flex-col">
          {view.kind === "discover" && <Discover onAsk={() => push({ kind: "result" })} />}

          {view.kind === "result" && (
            <Result
              cite={view.cite}
              isWide={isWide}
              onOpenCitation={(cite) => push({ kind: "result", cite })}
              onCloseCitation={back}
              onOpenInMail={() => push({ kind: "mail", threadId: sourceThreadId })}
              onBack={back}
            />
          )}

          {view.kind === "mail" && (
            <Mail
              threadId={view.threadId}
              isWide={isWide}
              onSelectThread={(threadId) =>
                // Selecting inside a two-pane layout is not a navigation step to undo.
                isWide ? replace({ kind: "mail", threadId }) : push({ kind: "mail", threadId })
              }
              onBack={back}
            />
          )}

          {view.kind === "cases" && <Cases />}
        </main>

        <TabBar active={sectionOf(view)} onSelect={goToSection} />
      </div>
    </div>
  );
}
