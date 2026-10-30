"use client";

import { Button } from "@/shared/ui/button";
import { Alert } from "@/shared/ui/primitives";

/** Falha inesperada numa página logada: mensagem clara e "tentar de novo", sem detalhes técnicos. */
export default function AppError({ reset }: { error: Error & { digest?: string }; reset: () => void }) {
  return (
    <Alert
      tone="danger"
      title="Não foi possível carregar esta página"
      action={
        <Button variant="secondary" size="sm" onClick={reset}>
          Tentar de novo
        </Button>
      }
    >
      Verifique a conexão. Se o problema continuar, tente novamente em alguns minutos.
    </Alert>
  );
}
