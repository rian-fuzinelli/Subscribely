const TIME_ZONE = "America/Sao_Paulo";

const group = new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 0 });

/** `R$ 1.234,56` a partir de centavos, sem ponto flutuante na conta (igual aos e-mails). */
export function formatMoney(cents: number): string {
  const sign = cents < 0 ? "-" : "";
  const abs = Math.abs(Math.trunc(cents));
  const reais = Math.trunc(abs / 100);
  const centavos = String(abs % 100).padStart(2, "0");
  return `${sign}R$ ${group.format(reais)},${centavos}`;
}

export function formatNumber(value: number): string {
  return group.format(value);
}

const date = new Intl.DateTimeFormat("pt-BR", { timeZone: TIME_ZONE, day: "2-digit", month: "2-digit", year: "numeric" });
const time = new Intl.DateTimeFormat("pt-BR", { timeZone: TIME_ZONE, hour: "2-digit", minute: "2-digit", hourCycle: "h23" });

/** `31/01/2026` no fuso de São Paulo. */
export function formatDate(value: string | Date): string {
  return date.format(new Date(value));
}

/** `31/01/2026 às 23:30` no fuso de São Paulo. */
export function formatDateTime(value: string | Date): string {
  const instant = new Date(value);
  return `${date.format(instant)} às ${time.format(instant)}`;
}

/** `janeiro de 2026` a partir de `2026-01`. */
export function formatPeriod(period: string): string {
  const [year, month] = period.split("-").map(Number);
  const label = new Intl.DateTimeFormat("pt-BR", { month: "long", year: "numeric", timeZone: "UTC" })
    .format(new Date(Date.UTC(year ?? 1970, (month ?? 1) - 1, 1)));
  return label;
}

/** Dias inteiros até `value` (arredonda para cima; 0 quando já passou). */
export function daysUntil(value: string | Date, now: Date = new Date()): number {
  return Math.max(0, Math.ceil((new Date(value).getTime() - now.getTime()) / 86_400_000));
}

export const intervalLabel: Record<string, string> = { monthly: "mensal", annual: "anual" };
