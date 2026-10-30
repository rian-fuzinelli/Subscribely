import "server-only";
import { redirect } from "next/navigation";
import { apiUrl } from "@/shared/config";
import { getAccessToken } from "@/shared/auth/session";
import { toProblem, unavailableProblem, type ApiProblem } from "./problem";

export type ApiResult<T> =
  | { ok: true; data: T; status: number; headers: Headers }
  | { ok: false; error: ApiProblem; headers?: Headers };

type Options = {
  method?: "GET" | "POST" | "PUT" | "DELETE";
  body?: unknown;
  /** Envia o access token da sessão (padrão). `false` para rotas públicas. */
  auth?: boolean;
  /** Em rotas autenticadas, 401 leva ao login (padrão). Desligue para tratar o 401 na tela. */
  redirectOnUnauthorized?: boolean;
};

/**
 * Chamada à API pelo servidor do portal. O token vem do cookie HttpOnly e nunca chega ao navegador.
 * Erros viram {@link ApiProblem} com mensagem pt-BR; rede fora do ar vira `unavailable`.
 */
export async function api<T = void>(path: string, options: Options = {}): Promise<ApiResult<T>> {
  const { method = "GET", body, auth = true, redirectOnUnauthorized = true } = options;
  const headers: Record<string, string> = { Accept: "application/json" };
  if (body !== undefined) {
    headers["Content-Type"] = "application/json";
  }

  if (auth) {
    const token = await getAccessToken();
    if (token) {
      headers.Authorization = `Bearer ${token}`;
    }
  }

  let response: Response;
  try {
    response = await fetch(`${apiUrl}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      cache: "no-store",
      signal: AbortSignal.timeout(15_000),
    });
  } catch {
    return { ok: false, error: unavailableProblem };
  }

  if (response.status === 401 && auth && redirectOnUnauthorized) {
    redirect("/login?expired=1");
  }

  const text = await response.text();
  const json: unknown = text ? safeJson(text) : undefined;
  if (!response.ok) {
    return { ok: false, error: toProblem(response.status, json), headers: response.headers };
  }

  return { ok: true, data: json as T, status: response.status, headers: response.headers };
}

/** Para Server Components: devolve os dados ou lança (a página de erro do segmento mostra a mensagem). */
export async function apiOrThrow<T>(path: string): Promise<T> {
  const result = await api<T>(path);
  if (!result.ok) {
    throw new Error(result.error.message);
  }

  return result.data;
}

function safeJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}
