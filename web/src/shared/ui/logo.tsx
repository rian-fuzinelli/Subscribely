import Link from "next/link";

/** Wordmark com o marcador quadrado do sistema (o mesmo dos status). */
export function Logo({ href = "/" }: { href?: string }) {
  return (
    <Link href={href} className="inline-flex items-baseline gap-1 rounded-control text-[1.0625rem] font-bold tracking-tight text-ink">
      subscribely
      <span aria-hidden className="size-1.5 bg-ledger-600" />
    </Link>
  );
}
