import { useId, useRef, useState, type KeyboardEvent, type ReactNode, type RefObject } from 'react';
import { useTranslation } from 'react-i18next';
import { useSelectionAfterRender } from '../../shared/useSelectionAfterRender';
import { RichText } from '../../shared/richText';
import { ToolButton } from '../../shared/components/ToolButton';

/** Composer code insertion and visual rich-text editing. */

type Action = 'bold' | 'italic' | 'strike' | 'code' | 'link' | 'list' | 'quote';

const codeLanguages = [
  ['bash', 'Bash'], ['c', 'C'], ['cpp', 'C++'], ['csharp', 'C#'], ['css', 'CSS'],
  ['html', 'HTML'], ['java', 'Java'], ['javascript', 'JavaScript'], ['json', 'JSON'], ['markdown', 'Markdown'],
  ['powershell', 'PowerShell'], ['python', 'Python'], ['sql', 'SQL'], ['typescript', 'TypeScript'], ['xml', 'XML'], ['yaml', 'YAML'],
] as const;

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

function editorHtmlToMessageText(root: HTMLElement): string {
  const safeUrl = (value: string) => {
    try {
      const url = new URL(value);
      return ['http:', 'https:', 'mailto:'].includes(url.protocol) ? url.href : null;
    } catch { return null; }
  };
  const text = (node: Node): string => {
    if (node.nodeType === Node.TEXT_NODE) return node.nodeValue ?? '';
    if (!(node instanceof HTMLElement)) return '';
    const children = () => [...node.childNodes].map(text).join('');
    if (node.classList.contains('cx-code-block')) {
      const code = node.querySelector('pre code')?.textContent ?? '';
      const language = node.dataset.language ?? '';
      const longestFence = Math.max(0, ...[...code.matchAll(/`{3,}/g)].map((match) => match[0].length));
      const fence = '`'.repeat(Math.max(3, longestFence + 1));
      return `${fence}${language}\n${code.replace(/\n+$/, '')}\n${fence}\n`;
    }
    switch (node.tagName.toLowerCase()) {
      case 'br': return '\n';
      case 'strong': case 'b': return `**${children()}**`;
      case 'em': case 'i': return `_${children()}_`;
      case 'del': case 's': case 'strike': return `~~${children()}~~`;
      case 'code': return node.parentElement?.tagName.toLowerCase() === 'pre' ? children() : `\`${children()}\``;
      case 'pre': {
        const code = node.textContent ?? '';
        const longestFence = Math.max(0, ...[...code.matchAll(/`{3,}/g)].map((match) => match[0].length));
        const fence = '`'.repeat(Math.max(3, longestFence + 1));
        return `${fence}\n${code.replace(/\n+$/, '')}\n${fence}\n`;
      }
      case 'a': {
        const href = safeUrl(node.getAttribute('href') ?? '');
        return href ? `[${children()}](${href})` : children();
      }
      case 'blockquote': return children().trimEnd().split('\n').map((line) => `> ${line}`).join('\n') + '\n';
      case 'ul': case 'ol': {
        const ordered = node.tagName.toLowerCase() === 'ol';
        let number = 1;
        return [...node.children].map((item) => `${ordered ? `${number++}.` : '-'} ${text(item).trim()}\n`).join('');
      }
      case 'li': return children();
      case 'p': case 'div': return `${children()}\n`;
      default: return children();
    }
  };
  return [...root.childNodes].map(text).join('').replace(/\n{3,}/g, '\n\n').trimEnd();
}

export interface Formatting {
  /** Kept in the composer key handler; plain-text formatting shortcuts are disabled. */
  onKeyDown: (event: KeyboardEvent<HTMLTextAreaElement>) => boolean;
  toolbar: ReactNode;
  codeDialog: ReactNode;
  richTextButton: ReactNode;
  richTextDialog: ReactNode;
}

export function useFormatting(textarea: RefObject<HTMLTextAreaElement | null>, value: string, setValue: (value: string) => void): Formatting {
  const { t } = useTranslation();
  const select = useSelectionAfterRender(textarea);
  const dialogId = useId();
  const [codeBlockOpen, setCodeBlockOpen] = useState(false);
  const [codeLanguage, setCodeLanguage] = useState('text');
  const [codeSource, setCodeSource] = useState('');
  const [richTextOpen, setRichTextOpen] = useState(false);
  const [richTextPreview, setRichTextPreview] = useState(false);
  const [richTextDraft, setRichTextDraft] = useState('');
  const [linkDialogOpen, setLinkDialogOpen] = useState(false);
  const [linkUrl, setLinkUrl] = useState('https://');
  const editorSurface = useRef<HTMLDivElement>(null);
  const richTextSource = useRef('');
  const savedEditorSelection = useRef<Range | null>(null);

  // The plain composer stays plain text; formatting shortcuts are available inside the rich editor.
  const onKeyDown = (_event: KeyboardEvent<HTMLTextAreaElement>) => false;

  const openRichTextEditor = () => {
    richTextSource.current = value;
    setRichTextDraft(value);
    setRichTextPreview(false);
    setLinkDialogOpen(false);
    setRichTextOpen(true);
  };

  const syncRichTextSource = () => {
    if (!editorSurface.current) return;
    richTextSource.current = editorHtmlToMessageText(editorSurface.current);
  };

  const applyEditorFormat = (action: Action) => {
    const surface = editorSurface.current;
    if (!surface) return;
    surface.focus();
    if (action === 'link') {
      const selection = window.getSelection();
      savedEditorSelection.current = selection?.rangeCount ? selection.getRangeAt(0).cloneRange() : null;
      setLinkUrl('https://');
      setLinkDialogOpen(true);
      return;
    }
    const command = action === 'bold' ? 'bold'
      : action === 'italic' ? 'italic'
        : action === 'strike' ? 'strikeThrough'
          : action === 'code' ? 'formatBlock'
            : action === 'list' ? 'insertUnorderedList'
              : 'formatBlock';
    document.execCommand(command, false, action === 'quote' ? 'blockquote' : action === 'code' ? 'pre' : undefined);
    syncRichTextSource();
  };

  const applyEditorLink = () => {
    let parsed: URL;
    try { parsed = new URL(linkUrl.trim()); } catch { return; }
    if (!['http:', 'https:', 'mailto:'].includes(parsed.protocol)) return;
    const selection = window.getSelection();
    if (savedEditorSelection.current && selection && editorSurface.current?.contains(savedEditorSelection.current.startContainer)) {
      selection.removeAllRanges();
      selection.addRange(savedEditorSelection.current);
    }
    editorSurface.current?.focus();
    document.execCommand('createLink', false, parsed.href);
    savedEditorSelection.current = null;
    setLinkDialogOpen(false);
    syncRichTextSource();
  };

  const onRichTextEditorKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const action = shortcut(event);
    if (!action) return;
    event.preventDefault();
    applyEditorFormat(action);
  };

  const insertCodeBlock = () => {
    const box = textarea.current;
    const start = box?.selectionStart ?? value.length;
    const end = box?.selectionEnd ?? value.length;
    const before = value.slice(0, start);
    const after = value.slice(end);
    const leading = before.length > 0 && !before.endsWith('\n') ? '\n' : '';
    const trailing = after.length > 0 && !after.startsWith('\n') ? '\n' : '';
    const block = `${leading}\`\`\`${codeLanguage === 'text' ? '' : codeLanguage}\n${codeSource}\n\`\`\`${trailing}`;
    setValue(before + block + after);
    const caret = before.length + block.length;
    select(caret, caret);
    setCodeBlockOpen(false);
    setCodeSource('');
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
      <ToolButton icon="code" className="cx-format-button is-code-block" label={t('format.codeBlock')}
        onMouseDown={(event) => event.preventDefault()} onClick={() => setCodeBlockOpen(true)} />
    </div>
  );

  const codeDialog = codeBlockOpen ? (
    <div className="cx-dialog-backdrop" onMouseDown={(e) => { if (e.target === e.currentTarget) setCodeBlockOpen(false); }}>
      <div className="cx-dialog cx-code-dialog" role="dialog" aria-modal="true" aria-labelledby={`${dialogId}-title`}
        onKeyDown={(e) => { if (e.key === 'Escape') { e.stopPropagation(); setCodeBlockOpen(false); } }}>
        <h2 id={`${dialogId}-title`} className="cx-confirm-title">{t('format.codeBlock')}</h2>
        <label className="form-label" htmlFor={`${dialogId}-language`}>{t('format.codeLanguage')}</label>
        <select id={`${dialogId}-language`} className="form-select mb-3" value={codeLanguage} onChange={(e) => setCodeLanguage(e.target.value)}>
          <option value="text">{t('format.plainText')}</option>
          {codeLanguages.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select>
        <label className="form-label" htmlFor={`${dialogId}-source`}>{t('format.codeContent')}</label>
        <textarea id={`${dialogId}-source`} className="form-control cx-code-editor" rows={12} spellCheck={false} autoCapitalize="off"
          autoComplete="off" autoCorrect="off" value={codeSource} onChange={(e) => setCodeSource(e.target.value)} />
        <div className="cx-confirm-actions">
          <button type="button" className="btn btn-outline-primary" onClick={() => setCodeBlockOpen(false)}>{t('confirm.cancel')}</button>
          <button type="button" className="btn btn-primary" onClick={insertCodeBlock}>{t('format.insertCode')}</button>
        </div>
      </div>
    </div>
  ) : null;

  const richTextButton = (
    <ToolButton icon="edit" className="cx-rich-editor-button" label={t('format.richTextEditor')} onClick={openRichTextEditor} />
  );

  const richTextDialog = richTextOpen ? (
        <div className="cx-dialog-backdrop">
      <div className="cx-dialog cx-rich-editor-dialog" role="dialog" aria-modal="true" aria-labelledby={`${dialogId}-rich-title`}
        onKeyDown={(event) => { if (event.key === 'Escape') { event.stopPropagation(); setRichTextOpen(false); } }}>
        <div className="cx-rich-editor-heading">
          <h2 id={`${dialogId}-rich-title`} className="cx-confirm-title">{t('format.richTextEditor')}</h2>
          <div className="btn-group btn-group-sm" role="group" aria-label={t('format.richTextView')}>
            <button type="button" className={`btn ${richTextPreview ? 'btn-outline-secondary' : 'btn-primary'}`} aria-pressed={!richTextPreview}
              onClick={() => setRichTextPreview(false)}>{t('format.edit')}</button>
            <button type="button" className={`btn ${richTextPreview ? 'btn-primary' : 'btn-outline-secondary'}`} aria-pressed={richTextPreview}
              onClick={() => { syncRichTextSource(); setRichTextDraft(richTextSource.current); setRichTextPreview(true); }}>{t('format.preview')}</button>
          </div>
        </div>
        {richTextPreview ? (
          <div className="cx-rich-editor-preview" aria-label={t('format.preview')}>
            {richTextDraft.trim() ? <RichText text={richTextDraft} /> : <p className="cx-muted">{t('format.emptyPreview')}</p>}
          </div>
        ) : (
          <>
            <div className="cx-format-bar cx-rich-editor-toolbar" role="toolbar" aria-label={t('format.label')}>
              {buttons.map((button) => (
                <button key={button.action} type="button" className={`cx-format-button is-${button.action}`} aria-label={button.label}
                  title={button.keys ? `${button.label} (${button.keys})` : button.label}
                  onMouseDown={(event) => { event.preventDefault(); const selection = window.getSelection();
                    savedEditorSelection.current = selection?.rangeCount ? selection.getRangeAt(0).cloneRange() : null; }}
                  onClick={() => applyEditorFormat(button.action)}>
                  <span aria-hidden="true">{button.glyph}</span>
                </button>
              ))}
            </div>
            <div ref={editorSurface} id={`${dialogId}-rich-content`} className="form-control cx-rich-editor-content cx-rich-editor-surface"
              role="textbox" aria-label={t('format.richTextContent')} aria-multiline="true" contentEditable suppressContentEditableWarning
              spellCheck onInput={syncRichTextSource}
              onPaste={(event) => { event.preventDefault(); document.execCommand('insertText', false, event.clipboardData.getData('text/plain')); syncRichTextSource(); }}
              onKeyDown={onRichTextEditorKeyDown}>
              <RichText text={richTextDraft} />
            </div>
          </>
        )}
        <div className="cx-confirm-actions">
          <button type="button" className="btn btn-outline-primary" onClick={() => setRichTextOpen(false)}>{t('confirm.cancel')}</button>
          <button type="button" className="btn btn-primary" onClick={() => { syncRichTextSource(); setValue(richTextSource.current); setRichTextOpen(false); }}>
            {t('format.apply')}
          </button>
        </div>
        {linkDialogOpen && (
          <div className="cx-rich-link-backdrop" role="dialog" aria-modal="true" aria-label={t('format.insertLink')}>
            <div className="cx-rich-link-dialog">
              <label className="form-label" htmlFor={`${dialogId}-link-url`}>{t('format.linkAddress')}</label>
              <input id={`${dialogId}-link-url`} className="form-control" type="url" value={linkUrl} onChange={(event) => setLinkUrl(event.target.value)}
                onKeyDown={(event) => { if (event.key === 'Enter') { event.preventDefault(); applyEditorLink(); } }} />
              <div className="cx-confirm-actions">
                <button type="button" className="btn btn-outline-primary" onClick={() => setLinkDialogOpen(false)}>{t('confirm.cancel')}</button>
                <button type="button" className="btn btn-primary" onClick={applyEditorLink}>{t('format.insertLink')}</button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  ) : null;

  return { onKeyDown, toolbar, codeDialog, richTextButton, richTextDialog };
}
