import Link from "next/link";

export function Logo({ href = "/" }: { href?: string }) {
  return (
    <Link href={href} className="inline-flex items-center gap-2 rounded-lg font-semibold text-slate-900">
      <span aria-hidden className="grid size-8 place-items-center rounded-lg bg-brand-600 text-sm font-bold text-white">
        S
      </span>
      <span>Subscribely</span>
    </Link>
  );
}
