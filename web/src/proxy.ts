import { NextResponse, type NextRequest } from "next/server";
import { ACCESS_COOKIE, REFRESH_COOKIE, REFRESH_MAX_AGE, cookieOptions, isProtected, type TokenPair } from "@/shared/auth/cookies";
import { decodeAccessToken, isExpiring } from "@/shared/auth/jwt";
import { apiUrl, secureCookies } from "@/shared/config";

/**
 * Antes de cada página: renova o access token pelo refresh (rotativo) quando ele expira em < 1 min e
 * manda para o login, com retorno, quem abre uma rota protegida sem sessão.
 */
export async function proxy(request: NextRequest) {
  const access = request.cookies.get(ACCESS_COOKIE)?.value;
  const refresh = request.cookies.get(REFRESH_COOKIE)?.value;
  const user = access ? decodeAccessToken(access) : null;

  if (isExpiring(user) && refresh) {
    const renewed = await renew(refresh);
    if (renewed) {
      // Atualiza o cookie da própria requisição (a página já renderiza com o token novo) e o da resposta.
      request.cookies.set(ACCESS_COOKIE, renewed.accessToken);
      request.cookies.set(REFRESH_COOKIE, renewed.refreshToken);
      const response = NextResponse.next({ request: { headers: request.headers } });
      response.cookies.set(ACCESS_COOKIE, renewed.accessToken, cookieOptions(secureCookies, renewed.expiresIn));
      response.cookies.set(REFRESH_COOKIE, renewed.refreshToken, cookieOptions(secureCookies, REFRESH_MAX_AGE));
      return response;
    }

    if (isProtected(request.nextUrl.pathname)) {
      const response = toLogin(request, true);
      response.cookies.delete(ACCESS_COOKIE);
      response.cookies.delete(REFRESH_COOKIE);
      return response;
    }
  }

  if (!user && !refresh && isProtected(request.nextUrl.pathname)) {
    return toLogin(request, false);
  }

  return NextResponse.next();
}

async function renew(refreshToken: string): Promise<TokenPair | null> {
  try {
    const response = await fetch(`${apiUrl}/v1/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken }),
      cache: "no-store",
      signal: AbortSignal.timeout(10_000),
    });
    return response.ok ? ((await response.json()) as TokenPair) : null;
  } catch {
    return null;
  }
}

function toLogin(request: NextRequest, expired: boolean) {
  const url = request.nextUrl.clone();
  url.pathname = "/login";
  url.search = "";
  url.searchParams.set("next", `${request.nextUrl.pathname}${request.nextUrl.search}`);
  if (expired) {
    url.searchParams.set("expired", "1");
  }

  return NextResponse.redirect(url);
}

export const config = {
  // Páginas, não assets nem Server Actions de formulários públicos.
  matcher: ["/((?!_next/static|_next/image|favicon.ico|.*\\.(?:svg|png|jpg|ico)$).*)"],
};
