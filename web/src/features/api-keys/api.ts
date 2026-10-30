import "server-only";
import { api } from "@/shared/api/client";

export type ApiKey = { id: string; name: string; prefix: string; createdAt: string; revokedAt: string | null };

export async function listApiKeys(): Promise<ApiKey[]> {
  const result = await api<ApiKey[]>("/v1/api-keys");
  return result.ok ? result.data : [];
}
