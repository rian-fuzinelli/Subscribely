"use client";

import { Pencil, Trash2 } from "lucide-react";
import { useState, useTransition, type FormEvent } from "react";
import { formatDateTime, formatNumber } from "@/shared/format";
import { Button, ButtonLink } from "@/shared/ui/button";
import { inputClass } from "@/shared/ui/field";
import { Alert, Card, CardTitle, EmptyState, Meter } from "@/shared/ui/primitives";
import { createProjectAction, deleteProjectAction, renameProjectAction } from "../actions";
import type { PlaygroundResult, Project, Quota } from "../model";

/** Usa o produto demo com a sessão do admin e mostra, a cada chamada, o efeito na quota. */
export function Playground({ initialProjects, initialQuota }: { initialProjects: Project[]; initialQuota: Quota | null }) {
  const [projects, setProjects] = useState(initialProjects);
  const [quota, setQuota] = useState(initialQuota);
  const [problem, setProblem] = useState<{ code: string; message: string } | null>(null);
  const [editing, setEditing] = useState<string | null>(null);
  const [pending, start] = useTransition();

  const apply = (result: PlaygroundResult) => {
    if (result.quota) {
      setQuota(result.quota);
    }

    if (!result.ok) {
      setProblem({ code: result.code, message: result.message });
      return false;
    }

    setProblem(null);
    if (result.deletedId) {
      setProjects((list) => list.filter((p) => p.id !== result.deletedId));
    } else if (result.project) {
      const project = result.project;
      setProjects((list) => (list.some((p) => p.id === project.id) ? list.map((p) => (p.id === project.id ? project : p)) : [project, ...list]));
    }

    return true;
  };

  const create = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const form = event.currentTarget;
    const data = new FormData(form);
    start(async () => {
      if (apply(await createProjectAction({ name: String(data.get("name") ?? ""), description: String(data.get("description") ?? "") || undefined }))) {
        form.reset();
      }
    });
  };

  return (
    <div className="grid gap-6 lg:grid-cols-[1fr_20rem]">
      <div className="space-y-6">
        {problem && (
          <Alert
            tone={problem.code === "quota-exceeded" || problem.code === "subscription-inactive" ? "warning" : "danger"}
            title={problem.message}
            action={
              problem.code === "quota-exceeded" ? (
                <ButtonLink href="/billing/change-plan" size="sm" variant="secondary">Fazer upgrade</ButtonLink>
              ) : problem.code === "subscription-inactive" ? (
                <ButtonLink href="/billing" size="sm" variant="secondary">Ver assinatura</ButtonLink>
              ) : undefined
            }
          >
            {problem.code === "quota-exceeded" && quota && "O contador zera em " + formatDateTime(quota.resetsAt) + "."}
          </Alert>
        )}

        <Card>
          <form onSubmit={create} className="space-y-4" aria-busy={pending}>
            <CardTitle>Novo projeto</CardTitle>
            <div className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-1.5">
                <label htmlFor="project-name" className="block text-sm font-medium text-slate-800">Nome</label>
                <input id="project-name" name="name" required maxLength={100} className={inputClass} />
              </div>
              <div className="space-y-1.5">
                <label htmlFor="project-description" className="block text-sm font-medium text-slate-800">Descrição (opcional)</label>
                <input id="project-description" name="description" maxLength={2_000} className={inputClass} />
              </div>
            </div>
            <div className="flex justify-end">
              <Button type="submit" variant="primary" disabled={pending}>Criar projeto</Button>
            </div>
          </form>
        </Card>

        <Card className="space-y-4">
          <CardTitle>Projetos</CardTitle>
          {projects.length === 0 ? (
            <EmptyState title="Nenhum projeto ainda" description="Crie o primeiro acima: cada ação conta uma requisição na sua quota." />
          ) : (
            <ul className="divide-y divide-slate-100">
              {projects.map((project) => (
                <li key={project.id} className="py-4">
                  {editing === project.id ? (
                    <form
                      className="flex flex-col gap-2 sm:flex-row"
                      onSubmit={(event) => {
                        event.preventDefault();
                        const name = String(new FormData(event.currentTarget).get("name") ?? "");
                        start(async () => {
                          if (apply(await renameProjectAction(project.id, name, project.description ?? undefined))) {
                            setEditing(null);
                          }
                        });
                      }}
                    >
                      <label htmlFor={"rename-" + project.id} className="sr-only">Novo nome</label>
                      <input id={"rename-" + project.id} name="name" defaultValue={project.name} autoFocus maxLength={100} className={inputClass} />
                      <div className="flex gap-2">
                        <Button type="submit" size="sm" variant="primary" disabled={pending}>Salvar</Button>
                        <Button size="sm" variant="ghost" onClick={() => setEditing(null)}>Cancelar</Button>
                      </div>
                    </form>
                  ) : (
                    <div className="flex items-start justify-between gap-4">
                      <div className="min-w-0">
                        <p className="truncate text-sm font-medium text-slate-900">{project.name}</p>
                        {project.description && <p className="truncate text-sm text-slate-600">{project.description}</p>}
                        <p className="text-xs text-slate-500">Atualizado em {formatDateTime(project.updatedAt)}</p>
                      </div>
                      <div className="flex shrink-0 gap-1">
                        <Button size="sm" variant="ghost" aria-label={"Renomear " + project.name} onClick={() => setEditing(project.id)}>
                          <Pencil className="size-4" aria-hidden />
                        </Button>
                        <Button
                          size="sm"
                          variant="ghost"
                          aria-label={"Excluir " + project.name}
                          disabled={pending}
                          onClick={() => start(async () => void apply(await deleteProjectAction(project.id)))}
                        >
                          <Trash2 className="size-4 text-red-600" aria-hidden />
                        </Button>
                      </div>
                    </div>
                  )}
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>

      <aside aria-label="Quota do mês">
        <Card className="space-y-3 lg:sticky lg:top-6">
          <CardTitle>Quota do mês</CardTitle>
          {quota ? (
            <>
              <p className="text-3xl font-bold tabular-nums text-slate-900" aria-live="polite">
                {formatNumber(quota.remaining)}
                <span className="text-base font-normal text-slate-500"> restantes</span>
              </p>
              <Meter label="Requisições usadas no mês" value={quota.limit - quota.remaining} max={quota.limit} />
              <p className="text-xs text-slate-500">
                Limite de {formatNumber(quota.limit)} por mês · zera em {formatDateTime(quota.resetsAt)}.
              </p>
            </>
          ) : (
            <p className="text-sm text-slate-600">Faça uma ação para ver a quota.</p>
          )}
          <p className="border-t border-slate-100 pt-3 text-xs text-slate-500">
            O playground chama a mesma API que os seus sistemas usam com uma chave: cada ação (inclusive abrir esta página) conta uma requisição.
          </p>
        </Card>
      </aside>
    </div>
  );
}
