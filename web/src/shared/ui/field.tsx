import { useId, type ComponentProps, type ReactNode } from "react";
import { cn } from "@/shared/cn";

type FieldProps = {
  label: string;
  name: string;
  hint?: ReactNode;
  errors?: string[];
} & Omit<ComponentProps<"input">, "name">;

export const inputClass =
  "block h-10 w-full rounded-control border border-line-strong bg-paper px-3 text-body text-ink " +
  "placeholder:text-ink-faint hover:border-ink-faint focus:border-ledger-600 focus:outline-2 focus:outline-offset-0 focus:outline-ledger-600/25 " +
  "aria-[invalid=true]:border-fault-600 aria-[invalid=true]:focus:outline-fault-600/25 disabled:bg-canvas disabled:text-ink-faint";

export const labelClass = "block text-meta font-medium text-ink";

/** Campo com rótulo, dica e erros ligados por `aria-describedby`. */
export function Field({ label, name, hint, errors, className, ...input }: FieldProps) {
  const id = useId();
  const hintId = hint ? id + "-hint" : undefined;
  const errorId = errors?.length ? id + "-error" : undefined;
  return (
    <div className={cn("space-y-1.5", className)}>
      <label htmlFor={id} className={labelClass}>
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
        <p id={hintId} className="text-meta text-ink-faint">
          {hint}
        </p>
      )}
      {errors?.length ? (
        <p id={errorId} className="text-meta font-medium text-fault-600">
          {errors.join(" ")}
        </p>
      ) : null}
    </div>
  );
}
