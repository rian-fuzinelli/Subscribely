export type Role = "TenantAdmin" | "Member" | "PlatformAdmin";

/** Dados de exibição tirados do access token. A API valida a assinatura; aqui só lemos o conteúdo. */
export type SessionUser = {
  userId: string;
  role: Role;
  tenantId: string | null;
  tenantSlug: string | null;
  expiresAt: number;
};

export function decodeAccessToken(token: string): SessionUser | null {
  const payload = token.split(".")[1];
  if (!payload) {
    return null;
  }

  try {
    const json = JSON.parse(Buffer.from(payload, "base64url").toString("utf8")) as Record<string, unknown>;
    if (typeof json.sub !== "string" || typeof json.role !== "string" || typeof json.exp !== "number") {
      return null;
    }

    return {
      userId: json.sub,
      role: json.role as Role,
      tenantId: typeof json.tid === "string" ? json.tid : null,
      tenantSlug: typeof json.tslug === "string" ? json.tslug : null,
      expiresAt: json.exp * 1000,
    };
  } catch {
    return null;
  }
}

/** Expira (ou expira em menos de `marginMs`) — hora de usar o refresh token. */
export function isExpiring(user: SessionUser | null, now = Date.now(), marginMs = 60_000): boolean {
  return !user || user.expiresAt - now < marginMs;
}
