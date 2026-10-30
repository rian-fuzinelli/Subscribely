import "server-only";

/** Base da API vista pelo servidor do portal (BFF). O navegador nunca fala com a API diretamente. */
export const apiUrl = (process.env.SUBSCRIBELY_API_URL ?? "http://localhost:5055").replace(/\/+$/, "");

/** Cookies `Secure` em produção; `COOKIE_SECURE=false` só para HTTP fora de localhost. */
export const secureCookies = process.env.COOKIE_SECURE
  ? process.env.COOKIE_SECURE === "true"
  : process.env.NODE_ENV === "production";
