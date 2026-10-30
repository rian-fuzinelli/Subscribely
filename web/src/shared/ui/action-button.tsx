"use client";

import { LoaderCircle } from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect, useId, useRef, useState, useTransition, type ReactNode } from "react";
import type { FormState } from "@/shared/forms";
import { Button, type ButtonStyle } from "./button";
import { Alert } from "./primitives";

type Confirm = { title: string; description: ReactNode; confirmLabel: string };

/**
 * Executa uma Server Action sem formulário (cancelar, revogar…). Com `confirm`, abre um diálogo modal
 * nativo (foco preso, Esc fecha) antes de agir. O resultado aparece logo abaixo do botão.
 */
export function ActionButton({
  action,
  children,
  confirm,
  watchFor = 0,
  destructive = false,
  variant = "secondary",
  size = "md",
  className,
}: {
  action: () => Promise<FormState>;
  children: ReactNode;
  confirm?: Confirm;
  /** Depois de sucesso, recarrega os dados da página a cada 2 s por este tempo (ms): pagamentos chegam por webhook. */
  watchFor?: number;
  /** Ação destrutiva: o botão de confirmação do diálogo fica vermelho, mesmo com o gatilho discreto. */
  destructive?: boolean;
} & ButtonStyle & { className?: string }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const [pending, start] = useTransition();
  const [result, setResult] = useState<FormState | null>(null);
  const router = useRouter();

  useEffect(() => {
    if (result?.status !== "success" || watchFor <= 0) {
      return;
    }

    const timer = setInterval(() => router.refresh(), 2_000);
    const stop = setTimeout(() => clearInterval(timer), watchFor);
    return () => {
      clearInterval(timer);
      clearTimeout(stop);
    };
  }, [result, watchFor, router]);

  const run = () =>
    start(async () => {
      dialog.current?.close();
      setResult(await action());
    });

  return (
    <div className="space-y-2">
      <Button
        variant={variant}
        size={size}
        className={className}
        disabled={pending}
        aria-busy={pending}
        onClick={() => (confirm ? dialog.current?.showModal() : run())}
      >
        {pending && <LoaderCircle className="size-4 animate-spin" aria-hidden />}
        {children}
      </Button>

      {result?.message && <Alert tone={result.status === "error" ? "danger" : "success"}>{result.message}</Alert>}

      {confirm && (
        <dialog
          ref={dialog}
          aria-labelledby={titleId}
          className="m-auto w-[min(28rem,calc(100%-2rem))] rounded-2xl p-0 shadow-xl backdrop:bg-slate-900/40"
        >
          <div className="p-6">
            <h2 id={titleId} className="text-lg font-semibold text-slate-900">
              {confirm.title}
            </h2>
            <div className="mt-2 text-sm text-slate-600">{confirm.description}</div>
            <div className="mt-6 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
              <Button variant="secondary" onClick={() => dialog.current?.close()} autoFocus>
                Voltar
              </Button>
              <Button variant={destructive || variant === "danger" ? "danger" : "primary"} onClick={run}>
                {confirm.confirmLabel}
              </Button>
            </div>
          </div>
        </dialog>
      )}
    </div>
  );
}
