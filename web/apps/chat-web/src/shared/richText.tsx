import { Fragment, type ReactNode } from 'react';

/**
 * Phase 19 (ADR-033): message formatting. Messages store the Markdown-style text people type; this
 * renders a small subset as React elements (never HTML strings): **bold**, *italic* / _italic_,
 * ~~strike~~, `code`, fenced code blocks, "> " quotes, "- " / "1. " lists, [text](url) and bare
 * http(s) links, and highlighted @mentions. Only http, https and mailto links are made clickable.
 */

export interface RichTextMentions {
  /** Display names to highlight after "@" (the people the message mentions). */
  names: string[];
  everyone?: boolean;
  here?: boolean;
  /** Your own name: its mention gets an extra highlight. */
  self?: string;
}

const safeHref = (url: string): string | null => {
  try {
    const parsed = new URL(url);
    return parsed.protocol === 'http:' || parsed.protocol === 'https:' || parsed.protocol === 'mailto:' ? parsed.href : null;
  } catch {
    return null;
  }
};

const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

type Rule = { re: RegExp; render: (m: RegExpExecArray, key: string, mentions: RichTextMentions | undefined) => ReactNode };

function rules(mentions: RichTextMentions | undefined): Rule[] {
  const list: Rule[] = [
    { re: /`([^`\n]+)`/y, render: (m, key) => <code key={key}>{m[1]}</code> },
    {
      re: /\[([^\]\n]+)\]\(((?:https?:\/\/|mailto:)[^\s)]+)\)/y,
      render: (m, key, men) => {
        const href = safeHref(m[2]!);
        return href ? (
          <a key={key} href={href} target="_blank" rel="noopener noreferrer nofollow">
            {inline(m[1]!, men, key)}
          </a>
        ) : (
          m[0]
        );
      },
    },
    {
      re: /(https?:\/\/[^\s<>"']*[^\s<>"'.,;:!?)\]])/y,
      render: (m, key) => {
        const href = safeHref(m[1]!);
        return href ? (
          <a key={key} href={href} target="_blank" rel="noopener noreferrer nofollow">
            {m[1]}
          </a>
        ) : (
          m[1]
        );
      },
    },
    { re: /(\*\*|__)(?=\S)([\s\S]+?)(?<=\S)\1/y, render: (m, key, men) => <strong key={key}>{inline(m[2]!, men, key)}</strong> },
    { re: /~~(?=\S)([\s\S]+?)(?<=\S)~~/y, render: (m, key, men) => <del key={key}>{inline(m[1]!, men, key)}</del> },
    { re: /([*_])(?=\S)([\s\S]+?)(?<=\S)\1(?![\p{L}\p{N}*_])/uy, render: (m, key, men) => <em key={key}>{inline(m[2]!, men, key)}</em> },
  ];
  const names = [...(mentions?.names ?? [])].filter(Boolean).sort((a, b) => b.length - a.length);
  if (mentions?.everyone) names.push('everyone');
  if (mentions?.here) names.push('here');
  if (names.length > 0) {
    list.push({
      re: new RegExp(`@(${names.map(escape).join('|')})(?![\\p{L}\\p{N}_])`, 'uy'),
      render: (m, key, men) => (
        <span key={key} className={`cx-mention${men?.self && m[1] === men.self ? ' is-self' : ''}`}>
          @{m[1]}
        </span>
      ),
    });
  }
  return list;
}

/** Characters that may start a formatted span; others are copied as text. */
const starters = new Set(['`', '[', 'h', '*', '_', '~', '@']);

function inline(text: string, mentions: RichTextMentions | undefined, prefix: string): ReactNode[] {
  const active = rules(mentions);
  const out: ReactNode[] = [];
  let plain = '';
  let i = 0;
  const flush = () => {
    if (plain) out.push(plain);
    plain = '';
  };
  while (i < text.length) {
    const ch = text[i]!;
    // Emphasis needs a word boundary before it (so snake_case and 2*3*4 stay as they are).
    const before = i === 0 ? ' ' : text[i - 1]!;
    const boundary = !/[\p{L}\p{N}*_~]/u.test(before);
    let matched = false;
    if (starters.has(ch)) {
      for (const rule of active) {
        if ((ch === '*' || ch === '_' || ch === '~' || ch === '@') && !boundary) break;
        rule.re.lastIndex = i;
        const m = rule.re.exec(text);
        if (m) {
          flush();
          out.push(rule.render(m, `${prefix}-${i}`, mentions));
          i += m[0].length;
          matched = true;
          break;
        }
      }
    }
    if (!matched) {
      plain += ch;
      i++;
    }
  }
  flush();
  return out;
}

type Block =
  | { kind: 'code'; text: string }
  | { kind: 'quote'; lines: string[] }
  | { kind: 'list'; ordered: boolean; items: string[] }
  | { kind: 'text'; lines: string[] };

const bullet = /^\s{0,3}[-*•]\s+(.*)$/;
const numbered = /^\s{0,3}\d{1,9}[.)]\s+(.*)$/;
const quote = /^\s{0,3}>\s?(.*)$/;

export function parseBlocks(text: string): Block[] {
  const lines = text.split('\n');
  const blocks: Block[] = [];
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i]!;
    if (line.trimStart().startsWith('```')) {
      const code: string[] = [];
      i++;
      while (i < lines.length && !lines[i]!.trimStart().startsWith('```')) code.push(lines[i++]!);
      blocks.push({ kind: 'code', text: code.join('\n') });
      continue;
    }
    const last = blocks.at(-1);
    let m: RegExpExecArray | null;
    if ((m = quote.exec(line))) {
      if (last?.kind === 'quote') last.lines.push(m[1]!);
      else blocks.push({ kind: 'quote', lines: [m[1]!] });
    } else if ((m = bullet.exec(line)) || (m = numbered.exec(line))) {
      const ordered = numbered.test(line) && !bullet.test(line);
      if (last?.kind === 'list' && last.ordered === ordered) last.items.push(m[1]!);
      else blocks.push({ kind: 'list', ordered, items: [m[1]!] });
    } else if (last?.kind === 'text') {
      last.lines.push(line);
    } else {
      blocks.push({ kind: 'text', lines: [line] });
    }
  }
  return blocks;
}

function lines(lines: string[], mentions: RichTextMentions | undefined, key: string): ReactNode {
  return lines.map((line, i) => (
    <Fragment key={`${key}-${i}`}>
      {i > 0 && <br />}
      {inline(line, mentions, `${key}-${i}`)}
    </Fragment>
  ));
}

/** Renders message text with formatting; safe for any input (no HTML is interpreted). */
export function RichText({ text, mentions, className }: { text: string; mentions?: RichTextMentions | undefined; className?: string }) {
  const blocks = parseBlocks(text);
  return (
    <div className={`cx-rich${className ? ` ${className}` : ''}`}>
      {blocks.map((block, b) => {
        const key = `b${b}`;
        switch (block.kind) {
          case 'code':
            return (
              <pre key={key} className="cx-code-block">
                <code>{block.text}</code>
              </pre>
            );
          case 'quote':
            return <blockquote key={key} className="cx-rich-quote">{lines(block.lines, mentions, key)}</blockquote>;
          case 'list': {
            const items = block.items.map((item, i) => <li key={`${key}-${i}`}>{inline(item, mentions, `${key}-${i}`)}</li>);
            return block.ordered ? <ol key={key}>{items}</ol> : <ul key={key}>{items}</ul>;
          }
          default:
            return <p key={key}>{lines(block.lines, mentions, key)}</p>;
        }
      })}
    </div>
  );
}

/** The text without formatting markers, for previews (matches the server's MessageText.ToPlain). */
export function toPlainText(text: string | null | undefined): string {
  if (!text) return '';
  let fence = false;
  const out: string[] = [];
  for (const raw of text.split('\n')) {
    if (raw.trimStart().startsWith('```')) {
      fence = !fence;
      continue;
    }
    let line = raw;
    if (!fence) {
      line = line
        .replace(/^\s{0,3}>\s?/, '')
        .replace(/`([^`\n]+)`/g, '$1')
        .replace(/\[([^\]\n]+)\]\(((?:https?:\/\/|mailto:)[^\s)]+)\)/g, (_m, label: string, url: string) => (label === url ? url : `${label} (${url})`))
        .replace(/(\*\*|__)(?=\S)(.+?)(?<=\S)\1/g, '$2')
        .replace(/~~(?=\S)(.+?)(?<=\S)~~/g, '$1')
        .replace(/(?<![\p{L}\p{N}*_])([*_])(?=\S)(.+?)(?<=\S)\1(?![\p{L}\p{N}*_])/gu, '$2');
    }
    out.push(line);
  }
  return out.join('\n').trim();
}
