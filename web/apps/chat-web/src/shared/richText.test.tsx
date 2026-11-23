import { describe, expect, it } from 'vitest';
import { render } from '@testing-library/react';
import { RichText, toPlainText } from './richText';
import { applyFormat } from '../features/conversations/formatting';

const html = (text: string, mentions?: Parameters<typeof RichText>[0]['mentions']) => render(<RichText text={text} mentions={mentions} />).container.innerHTML;

describe('RichText (Phase 19)', () => {
  it('renders the supported formatting', () => {
    const out = html('**bold** _it_ ~~gone~~ `x < 1`');
    expect(out).toContain('<strong>bold</strong>');
    expect(out).toContain('<em>it</em>');
    expect(out).toContain('<del>gone</del>');
    expect(out).toContain('<code>x &lt; 1</code>');
  });

  it('renders blocks: code, quotes and lists', () => {
    const { container } = render(<RichText text={'> said\n> twice\n- a\n- b\n1. one\n```\n**raw**\n```'} />);
    expect(container.querySelector('blockquote')?.textContent).toBe('saidtwice');
    expect([...container.querySelectorAll('ul li')].map((li) => li.textContent)).toEqual(['a', 'b']);
    expect(container.querySelector('ol li')?.textContent).toBe('one');
    expect(container.querySelector('pre code')?.textContent).toBe('**raw**');
  });

  it('never interprets HTML and only links safe addresses', () => {
    const { container } = render(<RichText text={'<img src=x onerror=alert(1)> [x](javascript:alert(1)) https://example.org/a_b?q=1. [docs](https://example.org/docs)'} />);
    expect(container.querySelector('img')).toBeNull();
    const links = [...container.querySelectorAll('a')];
    expect(links.map((a) => a.getAttribute('href'))).toEqual(['https://example.org/a_b?q=1', 'https://example.org/docs']);
    expect(links.every((a) => a.getAttribute('rel') === 'noopener noreferrer nofollow' && a.getAttribute('target') === '_blank')).toBe(true);
    expect(container.textContent).toContain('[x](javascript:alert(1))');
  });

  it('leaves ordinary text alone', () => {
    const { container } = render(<RichText text={'2 * 3 * 4, snake_case_name and a*b*c'} />);
    expect(container.querySelector('em, strong')).toBeNull();
    expect(container.textContent).toBe('2 * 3 * 4, snake_case_name and a*b*c');
  });

  it('highlights the mentioned people and you', () => {
    const { container } = render(<RichText text={'@Ana Diaz and @Ben, @everyone'} mentions={{ names: ['Ana Diaz', 'Ben'], everyone: true, self: 'Ben' }} />);
    const mentions = [...container.querySelectorAll('.cx-mention')];
    expect(mentions.map((m) => m.textContent)).toEqual(['@Ana Diaz', '@Ben', '@everyone']);
    expect(mentions[1]).toHaveClass('is-self');
  });

  it('gives plain text for previews', () => {
    expect(toPlainText('**Hi** [docs](https://example.org) `x`\n> quoted')).toBe('Hi docs (https://example.org) x\nquoted');
  });
});

describe('applyFormat (Phase 19)', () => {
  it('wraps the selection or inserts markers at the caret', () => {
    expect(applyFormat('bold', 'say hi', 4, 6)).toEqual({ value: 'say **hi**', start: 6, end: 8 });
    expect(applyFormat('italic', 'x', 1, 1)).toEqual({ value: 'x__', start: 2, end: 2 });
    expect(applyFormat('code', 'a\nb', 0, 3).value).toBe('```\na\nb\n```');
  });

  it('turns lines into a list or quote, and makes links', () => {
    expect(applyFormat('list', 'one\ntwo', 0, 7).value).toBe('- one\n- two');
    expect(applyFormat('quote', 'intro\nsaid', 6, 10).value).toBe('intro\n> said');
    const link = applyFormat('link', 'see docs', 4, 8);
    expect(link.value).toBe('see [docs](https://)');
    expect(link.value.slice(link.start, link.end)).toBe('https://');
  });
});
