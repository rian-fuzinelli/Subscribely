export type Project = { id: string; name: string; description: string | null; createdAt: string; updatedAt: string };

/** Estado da quota lido dos headers `X-RateLimit-*` da resposta do produto. */
export type Quota = { limit: number; remaining: number; resetsAt: string };

export function quotaFromHeaders(headers: Headers | undefined): Quota | null {
  const limit = Number(headers?.get("x-ratelimit-limit"));
  const remaining = Number(headers?.get("x-ratelimit-remaining"));
  const reset = Number(headers?.get("x-ratelimit-reset"));
  if (!headers || !Number.isFinite(limit) || !Number.isFinite(remaining) || !Number.isFinite(reset) || !headers.get("x-ratelimit-limit")) {
    return null;
  }

  return { limit, remaining, resetsAt: new Date(reset * 1000).toISOString() };
}

/** Resultado de cada ação do playground: o que mudou na lista e a quota depois da chamada. */
export type PlaygroundResult =
  | { ok: true; quota: Quota | null; project?: Project; deletedId?: string }
  | { ok: false; quota: Quota | null; code: string; message: string };
