import type { FormState } from "@/shared/forms";
import { Alert } from "./primitives";

/** Mensagem geral do formulário após a Server Action (erro ou sucesso). */
export function FormMessage({ state, success }: { state: FormState; success?: string }) {
  if (state.status === "error" && state.message) {
    return <Alert tone="danger">{state.message}</Alert>;
  }

  if (state.status === "success" && (success ?? state.message)) {
    return <Alert tone="success">{success ?? state.message}</Alert>;
  }

  return null;
}
