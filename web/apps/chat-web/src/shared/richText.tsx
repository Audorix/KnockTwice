import { Fragment, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';

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
  | { kind: 'code'; text: string; language: string }
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
      const language = line.trimStart().slice(3).trim().split(/\s+/, 1)[0] ?? '';
      const code: string[] = [];
      i++;
      while (i < lines.length && !lines[i]!.trimStart().startsWith('```')) code.push(lines[i++]!);
      blocks.push({ kind: 'code', text: code.join('\n'), language: /^[\w+#.-]{1,32}$/.test(language) ? language.toLowerCase() : '' });
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
  const { t } = useTranslation();
  const blocks = parseBlocks(text);
  return (
    <div className={`cx-rich${className ? ` ${className}` : ''}`}>
      {blocks.map((block, b) => {
        const key = `b${b}`;
        switch (block.kind) {
          case 'code':
            return <CodeBlock key={key} text={block.text} language={block.language} languageLabel={block.language === 'text' || !block.language ? t('format.plainText') : block.language}
              copyLabel={t('format.copyCode')} copiedLabel={t('format.copiedCode')} />;
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

function CodeBlock({ text, language, languageLabel, copyLabel, copiedLabel }: { text: string; language: string; languageLabel: string; copyLabel: string; copiedLabel: string }) {
  const [copied, setCopied] = useState(false);
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1800);
    } catch {
      setCopied(false);
    }
  };
  return (
    <div className="cx-code-block" data-language={language || undefined}>
      <div className="cx-code-header">
        <span className="cx-code-language">{languageLabel}</span>
        <button type="button" className="cx-code-copy" onClick={() => void copy()} aria-label={copied ? copiedLabel : copyLabel}>
          {copied ? copiedLabel : copyLabel}
        </button>
      </div>
      <pre><code>{highlightCode(text, language)}</code></pre>
    </div>
  );
}

type CodeToken = { text: string; kind: 'comment' | 'string' | 'number' | 'keyword' | 'literal' | 'property' | 'punctuation' | null };

const commonKeywords = new Set('as async await break case catch class const continue default delete do else export extends false finally for from function if import in interface let new null of package private protected public return static super switch this throw try typeof var void while with yield'.split(' '));
const languageKeywords: Record<string, Set<string>> = {
  bash: new Set('case do done elif else esac fi for function if in select then until while'.split(' ')),
  c: new Set('auto break case char const continue default do double else enum extern float for goto if int long register return short signed sizeof static struct switch typedef union unsigned void volatile while'.split(' ')),
  cpp: new Set('alignas auto bool break case char class const constexpr continue default delete do double else enum explicit extern false float for if int long namespace new nullptr private protected public return short signed sizeof static struct switch template this throw true try typedef typename union unsigned using virtual void volatile while'.split(' ')),
  csharp: new Set('abstract as async await bool break byte case catch char class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if int interface internal is lock long namespace new null object override params private protected public readonly ref return sbyte sealed short sizeof static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while'.split(' ')),
  java: new Set('abstract boolean break byte case catch char class const continue default do double else enum extends final finally float for if implements import instanceof int interface long native new package private protected public return short static strictfp super switch synchronized this throw throws transient try void volatile while'.split(' ')),
  javascript: commonKeywords,
  typescript: new Set([...commonKeywords, 'any', 'boolean', 'implements', 'keyof', 'never', 'number', 'readonly', 'string', 'type', 'unknown']),
  json: new Set(['true', 'false', 'null']),
  python: new Set('and as assert async await break class continue def del elif else except False finally for from global if import in is lambda None nonlocal not or pass raise return True try while with yield'.split(' ')),
  powershell: new Set('begin break catch class continue data do dynamicparam else elseif end exit filter finally for foreach from function if in param process return switch throw trap try until using while'.split(' ')),
  sql: new Set('add all alter as asc begin between by case cast check column commit constraint create cross current_date current_timestamp database delete desc distinct drop else end except exists foreign from full group having in index inner insert into is join key left like limit not null offset on or order outer primary references right rollback row select set table then top transaction union unique update values when where with'.split(' ')),
  yaml: new Set(['true', 'false', 'null', 'yes', 'no', 'on', 'off']),
};

/** A small, safe tokenizer for common snippets; unknown languages remain plain text. */
function highlightCode(text: string, language: string): ReactNode {
  const supported = new Set(['bash', 'c', 'cpp', 'csharp', 'css', 'html', 'java', 'javascript', 'json', 'powershell', 'python', 'sql', 'typescript', 'xml', 'yaml']);
  if (!supported.has(language)) return text;

  const hashComments = ['bash', 'python', 'powershell', 'yaml'].includes(language);
  const sqlComments = language === 'sql';
  const slashComments = ['c', 'cpp', 'csharp', 'java', 'javascript', 'typescript', 'css'].includes(language);
  const source = hashComments
    ? /(\s+|#.*$|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`|\b\d+(?:\.\d+)?\b|[\p{L}_$][\p{L}\p{N}_$-]*|[^\s])/gmu
    : sqlComments
      ? /(\s+|--.*$|\/\*[\s\S]*?\*\/|"(?:\\.|[^"\\])*"|'(?:''|\\.|[^'\\])*'|\b\d+(?:\.\d+)?\b|[\p{L}_$][\p{L}\p{N}_$]*|[^\s])/gimu
      : slashComments
        ? /(\s+|\/\/.*$|\/\*[\s\S]*?\*\/|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`|\b\d+(?:\.\d+)?\b|[\p{L}_$][\p{L}\p{N}_$]*|[^\s])/gmu
        : /(\s+|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`|\b\d+(?:\.\d+)?\b|[\p{L}_$][\p{L}\p{N}_$]*|[^\s])/gmu;
  const tokens: CodeToken[] = [];
  let match: RegExpExecArray | null;
  while ((match = source.exec(text))) {
    const value = match[0];
    let kind: CodeToken['kind'] | null = null;
    if ((hashComments && value.startsWith('#')) || (sqlComments && (value.startsWith('--') || value.startsWith('/*'))) || (slashComments && (value.startsWith('//') || value.startsWith('/*')))) kind = 'comment';
    else if (/^["'`]/.test(value)) kind = 'string';
    else if (/^\d/.test(value)) kind = 'number';
    else if (/^[\p{L}_$]/u.test(value)) {
      const lower = value.toLowerCase();
      const keywords = languageKeywords[language] ?? commonKeywords;
      const next = text.slice(source.lastIndex).match(/^\s*:/)?.[0];
      if (language === 'json' && next) kind = 'property';
      else if (['true', 'false', 'null', 'undefined', 'none'].includes(lower)) kind = 'literal';
      else if (keywords.has(language === 'sql' || language === 'yaml' ? lower : value)) kind = 'keyword';
    } else if (/^[{}()[\].,;:]/.test(value)) kind = 'punctuation';
    if (kind) tokens.push({ text: value, kind });
    else if (/^[{}()[\].,;:]+$/.test(value)) tokens.push({ text: value, kind: 'punctuation' });
    else tokens.push({ text: value, kind: null });
  }
  return tokens.map((token, index) => token.kind ? <span key={index} className={`cx-code-${token.kind}`}>{token.text}</span> : token.text);
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
