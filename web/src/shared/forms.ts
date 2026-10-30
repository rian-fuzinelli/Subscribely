import type { ZodError } from "zod";
import type { ApiProblem } from "@/shared/api/problem";

/** Estado devolvido por uma Server Action para o formulário (via `useActionState`). */
export type FormState = {
  status: "idle" | "error" | "success";
  message?: string;
  fieldErrors?: Record<string, string[]>;
  /** Valores enviados, para o formulário não perder o que foi digitado após um erro. */
  values?: Record<string, string>;
  /** Dados extras para a tela (ex.: a chave de API recém-criada). */
  data?: Record<string, unknown>;
};

export const idle: FormState = { status: "idle" };

export function formValues(form: FormData, ...names: string[]): Record<string, string> {
  return Object.fromEntries(names.map((name) => [name, String(form.get(name) ?? "")]));
}

export function fromZod(error: ZodError, values?: Record<string, string>): FormState {
  const fieldErrors: Record<string, string[]> = {};
  for (const issue of error.issues) {
    const key = String(issue.path[0] ?? "form");
    (fieldErrors[key] ??= []).push(issue.message);
  }

  return { status: "error", message: "Revise os campos destacados.", fieldErrors, values };
}

export function fromProblem(problem: ApiProblem, values?: Record<string, string>, field?: string): FormState {
  const fieldErrors = { ...problem.fieldErrors };
  if (field) {
    fieldErrors[field] = [problem.message];
  }

  return { status: "error", message: problem.message, fieldErrors, values };
}
