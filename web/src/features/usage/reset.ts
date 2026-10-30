/** Início do próximo período de quota (00:00 UTC do dia 1 do mês seguinte), a partir de `yyyy-MM`. */
export function quotaResetsAt(period: string): Date {
  const [year, month] = period.split("-").map(Number);
  return new Date(Date.UTC(year ?? 1970, month ?? 1, 1));
}

export function usagePercent(used: number, limit: number): number {
  return limit > 0 ? Math.round((used / limit) * 100) : 0;
}
