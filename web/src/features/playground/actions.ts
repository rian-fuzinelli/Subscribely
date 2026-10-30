"use server";

import { z } from "zod";
import { api } from "@/shared/api/client";
import { requireAdmin } from "@/shared/auth/session";
import { quotaFromHeaders, type PlaygroundResult, type Project } from "./model";

const projectSchema = z.object({
  name: z.string().trim().min(1, "Dê um nome ao projeto.").max(100, "Use no máximo 100 caracteres."),
  description: z.string().trim().max(2_000).optional(),
});

/**
 * Cada ação chama o produto uma vez (sem recarregar a lista, que gastaria outra requisição): a tela aplica
 * a mudança localmente e mostra a quota devolvida nos headers.
 */
async function call(path: string, method: "POST" | "PUT" | "DELETE", body?: unknown): Promise<PlaygroundResult & { data?: Project }> {
  await requireAdmin("/playground");
  const result = await api<Project>(path, { method, body });
  const quota = quotaFromHeaders(result.headers);
  return result.ok ? { ok: true, quota, data: result.data } : { ok: false, quota, code: result.error.code, message: result.error.message };
}

export async function createProjectAction(input: { name: string; description?: string }): Promise<PlaygroundResult> {
  const parsed = projectSchema.safeParse(input);
  if (!parsed.success) {
    return { ok: false, quota: null, code: "validation", message: parsed.error.issues[0]?.message ?? "Dados inválidos." };
  }

  const { data, ...result } = await call("/v1/projects", "POST", parsed.data);
  return result.ok ? { ...result, project: data } : result;
}

/** O `PUT` substitui nome e descrição: a descrição atual vai junto para não ser apagada. */
export async function renameProjectAction(id: string, name: string, description?: string): Promise<PlaygroundResult> {
  const parsed = projectSchema.safeParse({ name, description });
  if (!parsed.success) {
    return { ok: false, quota: null, code: "validation", message: parsed.error.issues[0]?.message ?? "Dados inválidos." };
  }

  const { data, ...result } = await call("/v1/projects/" + encodeURIComponent(id), "PUT", parsed.data);
  return result.ok ? { ...result, project: data } : result;
}

export async function deleteProjectAction(id: string): Promise<PlaygroundResult> {
  const result = await call("/v1/projects/" + encodeURIComponent(id), "DELETE");
  return result.ok ? { ok: true, quota: result.quota, deletedId: id } : result;
}
