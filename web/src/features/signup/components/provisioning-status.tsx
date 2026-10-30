"use client";

import { CircleCheck, CircleX, LoaderCircle } from "lucide-react";
import { useEffect, useState } from "react";
import { ButtonLink } from "@/shared/ui/button";
import { provisioningStatusAction, type ProvisioningStatus } from "../actions";

const POLL_MS = 2_000;
const GIVE_UP_MS = 120_000;

/** Acompanha o preparo da conta (banco do tenant) e libera o login quando ele termina. */
export function ProvisioningStatusPanel({ tenantId, email }: { tenantId: string; email?: string }) {
  const [status, setStatus] = useState<ProvisioningStatus>("Provisioning");
  const [timedOut, setTimedOut] = useState(false);

  useEffect(() => {
    let active = true;
    const started = Date.now();
    async function poll() {
      const next = await provisioningStatusAction(tenantId);
      if (!active) {
        return;
      }

      setStatus(next);
      if (next === "Provisioning" || next === "Unknown") {
        if (Date.now() - started > GIVE_UP_MS) {
          setTimedOut(true);
          return;
        }

        timer = setTimeout(poll, POLL_MS);
      }
    }

    let timer = setTimeout(poll, 0);
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [tenantId]);

  const loginHref = "/login?next=%2Foverview" + (email ? "&email=" + encodeURIComponent(email) : "");

  if (status === "Active") {
    return (
      <div role="status" className="space-y-4 text-center">
        <CircleCheck className="mx-auto size-12 text-ledger-700" aria-hidden />
        <h2 className="text-title font-semibold text-ink">Sua conta está pronta!</h2>
        <p className="text-meta text-ink-soft">O teste de 14 dias já começou. Entre para conhecer o portal.</p>
        <ButtonLink href={loginHref} size="lg">
          Entrar no portal
        </ButtonLink>
      </div>
    );
  }

  if (status === "ProvisioningFailed" || timedOut) {
    return (
      <div role="alert" className="space-y-3 text-center">
        <CircleX className="mx-auto size-12 text-fault-600" aria-hidden />
        <h2 className="text-title font-semibold text-ink">Não conseguimos preparar sua conta</h2>
        <p className="text-meta text-ink-soft">
          {timedOut
            ? "Está demorando mais que o normal. Tente entrar em alguns minutos."
            : "Tentamos algumas vezes sem sucesso. Fale com o suporte informando o e-mail usado no cadastro."}
        </p>
        <ButtonLink href={loginHref} variant="secondary">
          Tentar entrar
        </ButtonLink>
      </div>
    );
  }

  return (
    <div role="status" aria-live="polite" className="space-y-3 text-center">
      <LoaderCircle className="mx-auto size-12 animate-spin text-ledger-600" aria-hidden />
      <h2 className="text-title font-semibold text-ink">Preparando sua conta…</h2>
      <p className="text-meta text-ink-soft">Estamos criando um banco de dados exclusivo para a sua empresa. Leva só alguns segundos.</p>
    </div>
  );
}
