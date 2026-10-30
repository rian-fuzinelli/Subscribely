import "server-only";
import { api } from "@/shared/api/client";

export type Usage = { period: string; requestsUsed: number; requestLimit: number; seatsUsed: number; seatLimit: number };

export type UsageHistoryItem = { period: string; requestsCount: number; updatedAt: string };

export async function getUsage(): Promise<Usage | null> {
  const result = await api<Usage>("/v1/usage");
  return result.ok ? result.data : null;
}

export async function getUsageHistory(): Promise<UsageHistoryItem[]> {
  const result = await api<UsageHistoryItem[]>("/v1/usage/history");
  return result.ok ? result.data : [];
}
