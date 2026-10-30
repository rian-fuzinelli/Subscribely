import { z } from "zod";

export const loginSchema = z.object({
  email: z.email("Informe um e-mail válido."),
  password: z.string().min(1, "Informe a senha."),
});

export const forgotPasswordSchema = z.object({
  email: z.email("Informe um e-mail válido."),
});

const newPassword = z.string().min(12, "A senha precisa ter pelo menos 12 caracteres.").max(128, "Use no máximo 128 caracteres.");

/** Nova senha com confirmação (redefinição e aceite de convite). */
export const newPasswordSchema = z
  .object({ password: newPassword, confirmation: z.string() })
  .refine((value) => value.password === value.confirmation, { message: "As senhas não conferem.", path: ["confirmation"] });
