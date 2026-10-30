import { describe, expect, it } from "vitest";
import { messageFor, toProblem } from "./api/problem";
import { isProtected, safeNext } from "./auth/cookies";
import { decodeAccessToken, isExpiring } from "./auth/jwt";
import { daysUntil, formatDate, formatDateRange, formatDateTime, formatMoney, formatPeriod } from "./format";

describe("formatMoney", () => {
  it.each([
    [123_456, "R$ 1.234,56"],
    [4_900, "R$ 49,00"],
    [5, "R$ 0,05"],
    [100_000_000, "R$ 1.000.000,00"],
    [-2_450, "-R$ 24,50"],
  ])("%i centavos → %s (igual aos e-mails)", (cents, expected) => {
    expect(formatMoney(cents)).toBe(expected);
  });
});

describe("datas em São Paulo", () => {
  it("converte UTC para o fuso de Brasília", () => {
    expect(formatDateTime("2026-02-01T02:30:00Z")).toBe("31/01/2026 às 23:30");
    expect(formatDate("2026-02-01T02:30:00Z")).toBe("31/01/2026");
  });

  it("encurta o intervalo quando o ano é o mesmo", () => {
    expect(formatDateRange("2026-10-29T15:00:00Z", "2026-11-12T15:00:00Z")).toBe("29/10 a 12/11/2026");
    expect(formatDateRange("2026-12-20T15:00:00Z", "2027-01-20T15:00:00Z")).toBe("20/12/2026 a 20/01/2027");
  });

  it("nomeia o mês do período", () => {
    expect(formatPeriod("2026-03")).toBe("março de 2026");
  });

  it("conta os dias que faltam, nunca negativo", () => {
    const now = new Date("2026-01-01T00:00:00Z");
    expect(daysUntil("2026-01-03T12:00:00Z", now)).toBe(3);
    expect(daysUntil("2025-12-30T00:00:00Z", now)).toBe(0);
  });
});

describe("toProblem", () => {
  it("usa a mensagem pt-BR do código e guarda as extensões", () => {
    const problem = toProblem(409, { code: "seats-exceed-target-plan", detail: "internal", traceId: "x", activeSeats: 4, targetMaxSeats: 3 });

    expect(problem.code).toBe("seats-exceed-target-plan");
    expect(problem.message).toContain("plano escolhido");
    expect(problem.message).not.toContain("internal");
    expect(problem.details).toEqual({ activeSeats: 4, targetMaxSeats: 3 });
  });

  it("transforma erros de validação em erros por campo", () => {
    const problem = toProblem(400, { errors: { slug: ["Slug inválido"] } });

    expect(problem.code).toBe("validation");
    expect(problem.fieldErrors).toEqual({ slug: ["Slug inválido"] });
  });

  it("nunca expõe detalhe técnico para códigos desconhecidos", () => {
    expect(toProblem(500, { title: "NullReferenceException" }).message).toBe(messageFor("whatever", 500));
    expect(toProblem(403, undefined).message).toContain("permissão");
  });
});

describe("sessão", () => {
  const token = (payload: object) => "h." + Buffer.from(JSON.stringify(payload)).toString("base64url") + ".s";

  it("lê papel, tenant e expiração do access token", () => {
    const user = decodeAccessToken(token({ sub: "u1", role: "TenantAdmin", tid: "t1", tslug: "acme", exp: 2_000_000_000 }));

    expect(user).toEqual({ userId: "u1", role: "TenantAdmin", tenantId: "t1", tenantSlug: "acme", expiresAt: 2_000_000_000_000 });
  });

  it("rejeita tokens malformados", () => {
    expect(decodeAccessToken("garbage")).toBeNull();
    expect(decodeAccessToken(token({ role: "Member" }))).toBeNull();
  });

  it("renova faltando menos de 1 minuto", () => {
    const user = decodeAccessToken(token({ sub: "u", role: "Member", exp: 1_000 }))!;
    expect(isExpiring(user, 1_000_000 - 30_000)).toBe(true);
    expect(isExpiring(user, 1_000_000 - 120_000)).toBe(false);
    expect(isExpiring(null)).toBe(true);
  });

  it("só aceita retorno para caminhos internos (sem open redirect)", () => {
    expect(safeNext("/billing/invoices/1")).toBe("/billing/invoices/1");
    expect(safeNext("https://evil.test")).toBe("/overview");
    expect(safeNext("//evil.test")).toBe("/overview");
    expect(safeNext("/\\evil.test")).toBe("/overview");
    expect(safeNext(undefined)).toBe("/overview");
  });

  it("protege as rotas do portal, inclusive os destinos dos e-mails", () => {
    for (const path of ["/billing", "/billing/payment-method", "/billing/invoices/abc", "/usage", "/playground"]) {
      expect(isProtected(path)).toBe(true);
    }

    for (const path of ["/", "/login", "/signup", "/invitations/tok", "/password-reset", "/billingx"]) {
      expect(isProtected(path)).toBe(false);
    }
  });
});
