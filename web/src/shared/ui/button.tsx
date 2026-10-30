import Link from "next/link";
import type { ComponentProps } from "react";
import { cn } from "@/shared/cn";

const variants = {
  primary: "bg-ledger-600 text-paper hover:bg-ledger-700 active:bg-ledger-800 disabled:bg-ledger-600/45",
  secondary: "border border-line-strong bg-paper text-ink hover:border-ink-faint hover:bg-canvas disabled:text-ink-faint",
  danger: "bg-fault-600 text-paper hover:bg-fault-700 disabled:bg-fault-600/45",
  ghost: "text-ink-soft hover:bg-canvas hover:text-ink",
} as const;

const sizes = { sm: "h-8 px-3 text-meta", md: "h-10 px-4 text-body", lg: "h-11 px-5 text-body" } as const;

export type ButtonStyle = { variant?: keyof typeof variants; size?: keyof typeof sizes };

export function buttonClass({ variant = "primary", size = "md" }: ButtonStyle = {}, className?: string) {
  return cn(
    "inline-flex items-center justify-center gap-2 rounded-control font-medium whitespace-nowrap transition-colors duration-150",
    "disabled:cursor-not-allowed",
    variants[variant],
    sizes[size],
    className,
  );
}

export function Button({ variant, size, className, type = "button", ...props }: ComponentProps<"button"> & ButtonStyle) {
  return <button type={type} className={buttonClass({ variant, size }, className)} {...props} />;
}

export function ButtonLink({ variant, size, className, ...props }: ComponentProps<typeof Link> & ButtonStyle) {
  return <Link className={buttonClass({ variant, size }, className)} {...props} />;
}

/** Link de texto para ações secundárias dentro do conteúdo. */
export function TextLink({ className, ...props }: ComponentProps<typeof Link>) {
  return (
    <Link
      className={cn("font-medium text-ledger-700 underline decoration-ledger-600/30 underline-offset-4 hover:decoration-ledger-600", className)}
      {...props}
    />
  );
}
