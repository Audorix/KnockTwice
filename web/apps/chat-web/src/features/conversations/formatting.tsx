import type { KeyboardEvent, ReactNode, RefObject } from 'react';
import { useTranslation } from 'react-i18next';

/** Phase 19 (ADR-033): formatting in the composer, by toolbar or keyboard shortcut. */

type Action = 'bold' | 'italic' | 'strike' | 'code' | 'link' | 'list' | 'quote';

const wraps: Partial<Record<Action, string>> = { bold: '**', italic: '_', strike: '~~', code: '`' };

/**
 * Applies a formatting action to the textarea's selection: wraps it in markers (or inserts empty
 * markers with the caret between them), turns it into a link, or prefixes the selected lines.
 */
export function applyFormat(action: Action, value: string, start: number, end: number): { value: string; start: number; end: number } {
  const selected = value.slice(start, end);
  const marker = wraps[action];
  if (marker) {
    // Selections spanning lines become a code block rather than inline code.
    if (action === 'code' && selected.includes('\n')) {
      const block = `\`\`\`\n${selected}\n\`\`\``;
      return { value: value.slice(0, start) + block + value.slice(end), start: start + 4, end: start + 4 + selected.length };
    }
    return {
      value: value.slice(0, start) + marker + selected + marker + value.slice(end),
      start: start + marker.length,
      end: end + marker.length,
    };
  }
  if (action === 'link') {
    const label = selected || 'link';
    const text = `[${label}](https://)`;
    const urlStart = start + label.length + 3;
    return { value: value.slice(0, start) + text + value.slice(end), start: urlStart, end: urlStart + 8 };
  }
  // Line prefixes: from the start of the first selected line to the end of the last.
  const lineStart = value.lastIndexOf('\n', start - 1) + 1;
  const prefix = action === 'list' ? '- ' : '> ';
  const block = value.slice(lineStart, end);
  const prefixed = block
    .split('\n')
    .map((line) => (line.startsWith(prefix) ? line : prefix + line))
    .join('\n');
  return { value: value.slice(0, lineStart) + prefixed + value.slice(end), start: lineStart, end: lineStart + prefixed.length };
}

const shortcut = (event: KeyboardEvent): Action | null => {
  if (!(event.ctrlKey || event.metaKey) || event.altKey) return null;
  switch (event.key.toLowerCase()) {
    case 'b':
      return event.shiftKey ? null : 'bold';
    case 'i':
      return event.shiftKey ? null : 'italic';
    case 'x':
      return event.shiftKey ? 'strike' : null;
    case 'e':
      return event.shiftKey ? null : 'code';
    case 'k':
      return event.shiftKey ? null : 'link';
    default:
      return null;
  }
};

export interface Formatting {
  /** Call first in the textarea's key handler; true when a shortcut was applied. */
  onKeyDown: (event: KeyboardEvent<HTMLTextAreaElement>) => boolean;
  toolbar: ReactNode;
}

export function useFormatting(textarea: RefObject<HTMLTextAreaElement | null>, value: string, setValue: (value: string) => void): Formatting {
  const { t } = useTranslation();

  const run = (action: Action) => {
    const box = textarea.current;
    const start = box?.selectionStart ?? value.length;
    const end = box?.selectionEnd ?? value.length;
    const next = applyFormat(action, value, start, end);
    setValue(next.value);
    requestAnimationFrame(() => {
      if (!box) return;
      box.focus();
      box.setSelectionRange(next.start, next.end);
    });
  };

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    const action = shortcut(event);
    if (!action) return false;
    event.preventDefault();
    run(action);
    return true;
  };

  const buttons: { action: Action; label: string; glyph: string; keys: string }[] = [
    { action: 'bold', label: t('format.bold'), glyph: 'B', keys: 'Ctrl+B' },
    { action: 'italic', label: t('format.italic'), glyph: 'I', keys: 'Ctrl+I' },
    { action: 'strike', label: t('format.strike'), glyph: 'S', keys: 'Ctrl+Shift+X' },
    { action: 'code', label: t('format.code'), glyph: '</>', keys: 'Ctrl+E' },
    { action: 'link', label: t('format.link'), glyph: '🔗', keys: 'Ctrl+K' },
    { action: 'list', label: t('format.list'), glyph: '•', keys: '' },
    { action: 'quote', label: t('format.quote'), glyph: '❝', keys: '' },
  ];

  const toolbar = (
    <div className="cx-format-bar" role="toolbar" aria-label={t('format.label')}>
      {buttons.map((b) => (
        <button
          key={b.action}
          type="button"
          className={`cx-format-button is-${b.action}`}
          aria-label={b.label}
          title={b.keys ? `${b.label} (${b.keys})` : b.label}
          onMouseDown={(e) => e.preventDefault()} // keep the selection in the textarea
          onClick={() => run(b.action)}
        >
          <span aria-hidden="true">{b.glyph}</span>
        </button>
      ))}
    </div>
  );

  return { onKeyDown, toolbar };
}
