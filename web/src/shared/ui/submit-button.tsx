"use client";

import { LoaderCircle } from "lucide-react";
import { useFormStatus } from "react-dom";
import type { ComponentProps } from "react";
import { Button, type ButtonStyle } from "./button";

/** Botão de envio que desabilita e mostra progresso enquanto a Server Action roda. */
export function SubmitButton({
  children,
  pendingLabel,
  ...props
}: ComponentProps<"button"> & ButtonStyle & { pendingLabel?: string }) {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" {...props} disabled={pending || props.disabled} aria-busy={pending}>
      {pending && <LoaderCircle className="size-4 animate-spin" aria-hidden />}
      {pending ? (pendingLabel ?? children) : children}
    </Button>
  );
}
