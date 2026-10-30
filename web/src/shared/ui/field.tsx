import { useId, type ComponentProps, type ReactNode } from "react";
import { cn } from "@/shared/cn";

type FieldProps = {
  label: string;
  name: string;
  hint?: ReactNode;
  errors?: string[];
} & Omit<ComponentProps<"input">, "name">;

export const inputClass =
  "block h-10 w-full rounded-lg border border-slate-300 bg-white px-3 text-sm text-slate-900 shadow-xs " +
  "placeholder:text-slate-400 focus:border-brand-600 focus:outline-2 focus:outline-brand-600/30 " +
  "aria-[invalid=true]:border-red-500 aria-[invalid=true]:focus:outline-red-500/30 disabled:bg-slate-50";

/** Campo com rótulo, dica e erros ligados por `aria-describedby`. */
export function Field({ label, name, hint, errors, className, ...input }: FieldProps) {
  const id = useId();
  const hintId = hint ? id + "-hint" : undefined;
  const errorId = errors?.length ? id + "-error" : undefined;
  return (
    <div className={cn("space-y-1.5", className)}>
      <label htmlFor={id} className="block text-sm font-medium text-slate-800">
        {label}
      </label>
      <input
        id={id}
        name={name}
        aria-invalid={errors?.length ? true : undefined}
        aria-describedby={[hintId, errorId].filter(Boolean).join(" ") || undefined}
        className={inputClass}
        {...input}
      />
      {hint && (
        <p id={hintId} className="text-xs text-slate-500">
          {hint}
        </p>
      )}
      {errors?.length ? (
        <p id={errorId} className="text-xs font-medium text-red-600">
          {errors.join(" ")}
        </p>
      ) : null}
    </div>
  );
}
