import "server-only";
import { api } from "@/shared/api/client";
import { quotaFromHeaders, type Project, type Quota } from "./model";

export type PlaygroundLoad =
  | { ok: true; projects: Project[]; quota: Quota | null }
  | { ok: false; code: string; message: string; quota: Quota | null };

/** Lista os projetos com a sessão do admin. Conta como uma requisição na quota, como qualquer chamada ao produto. */
export async function loadPlayground(): Promise<PlaygroundLoad> {
  const result = await api<Project[]>("/v1/projects");
  return result.ok
    ? { ok: true, projects: result.data, quota: quotaFromHeaders(result.headers) }
    : { ok: false, code: result.error.code, message: result.error.message, quota: quotaFromHeaders(result.headers) };
}
