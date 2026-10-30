import { describe, expect, it } from "vitest";
import { describeAction } from "./audit/labels";
import { isTestCard } from "./payment-method/cards";
import { quotaFromHeaders } from "./playground/model";
import { signupSchema, suggestSlug } from "./signup/schema";
import { previewPlanChange, productAvailable } from "./subscription/model";
import { quotaResetsAt, usagePercent } from "./usage/reset";

const starter = { code: "starter", monthlyPriceCents: 4_900, annualPriceCents: 49_000 };
const pro = { code: "pro", monthlyPriceCents: 14_900, annualPriceCents: 149_000 };
const twin = { code: "twin", monthlyPriceCents: 4_900, annualPriceCents: 49_000 };

describe("previewPlanChange (mesma regra da API)", () => {
  it("no trial, qualquer mudança é imediata e sem cobrança", () => {
    expect(previewPlanChange("Trialing", { ...pro, interval: "monthly" }, starter, "monthly")).toBe("immediate-free");
  });

  it("upgrade no mesmo intervalo é imediato e proporcional", () => {
    expect(previewPlanChange("Active", { ...starter, interval: "monthly" }, pro, "monthly")).toBe("immediate-prorated");
  });

  it("mensal → anual é upgrade mesmo no mesmo plano; anual → mensal é downgrade", () => {
    expect(previewPlanChange("Active", { ...starter, interval: "monthly" }, starter, "annual")).toBe("immediate-prorated");
    expect(previewPlanChange("Active", { ...pro, interval: "annual" }, pro, "monthly")).toBe("period-end");
  });

  it("downgrade fica para o fim do período; mesmo preço é imediato sem cobrança", () => {
    expect(previewPlanChange("Active", { ...pro, interval: "monthly" }, starter, "monthly")).toBe("period-end");
    expect(previewPlanChange("Active", { ...starter, interval: "monthly" }, twin, "monthly")).toBe("immediate-free");
  });

  it("upgrade com pagamento pendente é bloqueado; mesmo plano e intervalo não muda nada", () => {
    expect(previewPlanChange("PastDue", { ...starter, interval: "monthly" }, pro, "monthly")).toBe("blocked-past-due");
    expect(previewPlanChange("Active", { ...starter, interval: "monthly" }, starter, "monthly")).toBe("no-change");
  });

  it("o produto só funciona em teste, ativa ou com pagamento pendente", () => {
    expect(["Trialing", "Active", "PastDue"].every((s) => productAvailable(s as never))).toBe(true);
    expect(["Suspended", "Expired", "Canceled"].some((s) => productAvailable(s as never))).toBe(false);
  });
});

describe("signup", () => {
  it("sugere o identificador a partir do nome da empresa", () => {
    expect(suggestSlug("Açaí & Cia. Ltda")).toBe("acai-cia-ltda");
    expect(suggestSlug("  ")).toBe("");
  });

  it("valida com as mesmas regras da API", () => {
    const valid = { companyName: "Acme", slug: "acme", adminEmail: "a@acme.test", password: "x".repeat(12), planCode: "starter", interval: "monthly" };
    expect(signupSchema.safeParse(valid).success).toBe(true);
    expect(signupSchema.safeParse({ ...valid, slug: "Acme!" }).success).toBe(false);
    expect(signupSchema.safeParse({ ...valid, password: "short" }).success).toBe(false);
    expect(signupSchema.safeParse({ ...valid, interval: "weekly" }).success).toBe(false);
  });
});

describe("quota", () => {
  it("lê os headers X-RateLimit do produto", () => {
    const headers = new Headers({ "X-RateLimit-Limit": "100", "X-RateLimit-Remaining": "7", "X-RateLimit-Reset": "1769904000" });

    expect(quotaFromHeaders(headers)).toEqual({ limit: 100, remaining: 7, resetsAt: "2026-02-01T00:00:00.000Z" });
    expect(quotaFromHeaders(new Headers())).toBeNull();
  });

  it("zera no dia 1 do mês seguinte (UTC), inclusive na virada do ano", () => {
    expect(quotaResetsAt("2026-01").toISOString()).toBe("2026-02-01T00:00:00.000Z");
    expect(quotaResetsAt("2026-12").toISOString()).toBe("2027-01-01T00:00:00.000Z");
    expect(usagePercent(80, 100)).toBe(80);
    expect(usagePercent(5, 0)).toBe(0);
  });
});

describe("rótulos", () => {
  it("descreve as ações da auditoria em português", () => {
    expect(describeAction("subscription.updated")).toBe("alterou assinatura");
    expect(describeAction("api_key.created")).toBe("criou chave de API");
  });

  it("aceita só os cartões de teste do gateway simulado", () => {
    expect(isTestCard("pm_card_ok")).toBe(true);
    expect(isTestCard("4111111111111111")).toBe(false);
  });
});
