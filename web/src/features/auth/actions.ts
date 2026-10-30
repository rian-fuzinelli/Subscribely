"use server";

import { redirect } from "next/navigation";
import { api } from "@/shared/api/client";
import { safeNext, type TokenPair } from "@/shared/auth/cookies";
import { decodeAccessToken } from "@/shared/auth/jwt";
import { clearSession, getRefreshToken, setSession } from "@/shared/auth/session";
import { formValues, fromProblem, fromZod, type FormState } from "@/shared/forms";
import { forgotPasswordSchema, loginSchema, newPasswordSchema } from "./schema";

export async function loginAction(_: FormState, form: FormData): Promise<FormState> {
  const values = formValues(form, "email", "password", "next");
  const echo = { email: values.email ?? "", next: values.next ?? "" };
  const parsed = loginSchema.safeParse(values);
  if (!parsed.success) {
    return fromZod(parsed.error, echo);
  }

  const result = await api<TokenPair>("/v1/auth/login", { method: "POST", body: parsed.data, auth: false });
  if (!result.ok) {
    return fromProblem(result.error, echo);
  }

  if (decodeAccessToken(result.data.accessToken)?.role === "PlatformAdmin") {
    return {
      status: "error",
      message: "O portal é para as contas clientes. Administradores da plataforma usam as ferramentas administrativas.",
      values: echo,
    };
  }

  await setSession(result.data, parsed.data.email);
  redirect(safeNext(values.next));
}

export async function logoutAction(): Promise<void> {
  const refreshToken = await getRefreshToken();
  if (refreshToken) {
    // Revoga a família de refresh tokens na API; a sessão local some de qualquer forma.
    await api("/v1/auth/logout", { method: "POST", body: { refreshToken }, auth: false });
  }

  await clearSession();
  redirect("/login?loggedOut=1");
}

export async function forgotPasswordAction(_: FormState, form: FormData): Promise<FormState> {
  const values = formValues(form, "email");
  const parsed = forgotPasswordSchema.safeParse(values);
  if (!parsed.success) {
    return fromZod(parsed.error, values);
  }

  // A API responde igual exista ou não o e-mail; o portal também.
  await api("/v1/auth/password-reset", { method: "POST", body: parsed.data, auth: false });
  return {
    status: "success",
    message: "Se houver uma conta com esse e-mail, você vai receber um link para redefinir a senha em instantes.",
    values,
  };
}

export async function resetPasswordAction(_: FormState, form: FormData): Promise<FormState> {
  const values = formValues(form, "password", "confirmation", "token");
  const parsed = newPasswordSchema.safeParse(values);
  if (!parsed.success) {
    return fromZod(parsed.error);
  }

  const result = await api("/v1/auth/password-reset/confirm", {
    method: "POST",
    body: { token: values.token, newPassword: parsed.data.password },
    auth: false,
  });
  if (!result.ok) {
    return fromProblem(result.error);
  }

  await clearSession();
  redirect("/login?reset=1");
}

export async function acceptInvitationAction(_: FormState, form: FormData): Promise<FormState> {
  const values = formValues(form, "password", "confirmation", "token");
  const parsed = newPasswordSchema.safeParse(values);
  if (!parsed.success) {
    return fromZod(parsed.error);
  }

  const token = encodeURIComponent(values.token ?? "");
  const result = await api("/v1/invitations/" + token + "/accept", {
    method: "POST",
    body: { password: parsed.data.password },
    auth: false,
  });
  if (!result.ok) {
    return fromProblem(result.error.status === 410 || result.error.status === 404
      ? { ...result.error, message: "Este convite expirou ou já foi usado. Peça um novo convite ao administrador da conta." }
      : result.error);
  }

  redirect("/login?invited=1");
}
