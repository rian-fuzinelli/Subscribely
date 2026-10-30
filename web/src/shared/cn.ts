import { clsx, type ClassValue } from "clsx";
import { extendTailwindMerge } from "tailwind-merge";

/**
 * O tailwind-merge precisa conhecer a escala tipográfica do tema (`text-meta`, `text-body`…); senão ele as
 * confunde com cores e descarta, por exemplo, o `text-paper` dos botões.
 */
const twMerge = extendTailwindMerge({
  extend: {
    theme: {
      text: ["meta", "body", "title", "display"],
      radius: ["control", "surface"],
    },
  },
});

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}
