/** Nomes e regras dos cookies de sessão (compartilhado entre o proxy e o servidor). */
export const ACCESS_COOKIE = "sb_at";
export const REFRESH_COOKIE = "sb_rt";
export const EMAIL_COOKIE = "sb_email";

export const REFRESH_MAX_AGE = 7 * 24 * 60 * 60;

export type TokenPair = { accessToken: string; expiresIn: number; refreshToken: string };

export function cookieOptions(secure: boolean, maxAge: number) {
  return { httpOnly: true, sameSite: "lax" as const, secure, path: "/", maxAge };
}

/** Rotas que exigem sessão (prefixos). Os links dos e-mails caem aqui e voltam após o login. */
export const PROTECTED_PREFIXES = ["/overview", "/billing", "/usage", "/team", "/api-keys", "/audit", "/playground"];

export function isProtected(pathname: string): boolean {
  return PROTECTED_PREFIXES.some((prefix) => pathname === prefix || pathname.startsWith(`${prefix}/`));
}

/** Só caminhos internos (evita open redirect no `?next=`). */
export function safeNext(next: string | null | undefined, fallback = "/overview"): string {
  return next && next.startsWith("/") && !next.startsWith("//") && !next.startsWith("/\\") ? next : fallback;
}
