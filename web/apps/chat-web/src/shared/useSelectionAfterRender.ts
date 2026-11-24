import { useCallback, useLayoutEffect, useRef, useState, type RefObject } from 'react';

/**
 * Puts the cursor (or a selection) in a text box right after React has rendered the new text, and
 * before the browser handles the next key. Waiting for an animation frame instead let fast typing
 * land in the wrong place: the next characters arrived first, then the frame moved the cursor back
 * (seen as "Hi @carl ee thiss" on fast CI runners).
 */
export function useSelectionAfterRender(target: RefObject<HTMLTextAreaElement | HTMLInputElement | null>) {
  const pending = useRef<{ start: number; end: number } | null>(null);
  const [, rerender] = useState(0);

  useLayoutEffect(() => {
    const selection = pending.current;
    const box = target.current;
    if (!selection || !box) return;
    pending.current = null;
    box.focus();
    box.setSelectionRange(selection.start, selection.end);
  });

  return useCallback((start: number, end: number = start) => {
    pending.current = { start, end };
    // Render even when the text did not change, so the selection is still applied.
    rerender((n) => n + 1);
  }, []);
}
