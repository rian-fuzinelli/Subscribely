import "server-only";
import { api } from "@/shared/api/client";

export type TeamUser = { id: string; email: string; role: "TenantAdmin" | "Member"; status: "Active" | "Disabled" };

export type Invitation = { id: string; email: string; role: "TenantAdmin" | "Member"; expiresAt: string };

export async function listUsers(): Promise<TeamUser[]> {
  const result = await api<TeamUser[]>("/v1/users");
  return result.ok ? result.data : [];
}

export async function listInvitations(): Promise<Invitation[]> {
  const result = await api<Invitation[]>("/v1/users/invitations");
  return result.ok ? result.data : [];
}
