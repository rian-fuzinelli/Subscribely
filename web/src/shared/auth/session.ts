import "server-only";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { secureCookies } from "@/shared/config";
import { ACCESS_COOKIE, EMAIL_COOKIE, REFRESH_COOKIE, REFRESH_MAX_AGE, cookieOptions, type TokenPair } from "./cookies";
import { decodeAccessToken, type SessionUser } from "./jwt";

export type Session = SessionUser & { email: string | null };

export async function getAccessToken(): Promise<string | null> {
  return (await cookies()).get(ACCESS_COOKIE)?.value ?? null;
}

export async function getRefreshToken(): Promise<string | null> {
  return (await cookies()).get(REFRESH_COOKIE)?.value ?? null;
}

export async function getSession(): Promise<Session | null> {
  const store = await cookies();
  const token = store.get(ACCESS_COOKIE)?.value;
  const user = token ? decodeAccessToken(token) : null;
  return user ? { ...user, email: store.get(EMAIL_COOKIE)?.value ?? null } : null;
}

/** Grava os tokens em cookies HttpOnly (só em Server Actions / Route Handlers). */
export async function setSession(tokens: TokenPair, email?: string): Promise<void> {
  const store = await cookies();
  store.set(ACCESS_COOKIE, tokens.accessToken, cookieOptions(secureCookies, tokens.expiresIn));
  store.set(REFRESH_COOKIE, tokens.refreshToken, cookieOptions(secureCookies, REFRESH_MAX_AGE));
  if (email) {
    store.set(EMAIL_COOKIE, email, cookieOptions(secureCookies, REFRESH_MAX_AGE));
  }
}

export async function clearSession(): Promise<void> {
  const store = await cookies();
  for (const name of [ACCESS_COOKIE, REFRESH_COOKIE, EMAIL_COOKIE]) {
    store.delete(name);
  }
}

/** Exige usuário de tenant logado; senão, login com retorno para `next`. */
export async function requireSession(next: string): Promise<Session> {
  const session = await getSession();
  if (!session) {
    redirect(`/login?next=${encodeURIComponent(next)}`);
  }

  if (session.role === "PlatformAdmin" || !session.tenantId) {
    redirect("/login?reason=platform-admin");
  }

  return session;
}

/** Exige TenantAdmin; Members veem a página de "sem permissão". */
export async function requireAdmin(next: string): Promise<Session> {
  const session = await requireSession(next);
  if (session.role !== "TenantAdmin") {
    redirect("/overview?denied=1");
  }

  return session;
}
