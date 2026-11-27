import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useEffect, useRef, useState } from 'react';
import { RichText, toPlainText } from './richText';
import { applyFormat, useFormatting } from '../features/conversations/formatting';
import i18n, { initI18n } from '../i18n/i18n';

beforeAll(async () => {
  await initI18n();
});

beforeEach(async () => {
  await i18n.changeLanguage('en');
});

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

  it('preserves fenced code whitespace and highlights common TypeScript tokens', () => {
    const code = 'const answer = "yes";\nconsole.log(answer);';
    const { container } = render(<RichText text={`\`\`\`typescript\n${code}\n\`\`\``} />);
    const block = container.querySelector('.cx-code-block');
    expect(block).toHaveAttribute('data-language', 'typescript');
    expect(block?.textContent).toContain(code);
    expect(block?.querySelector('.cx-code-keyword')?.textContent).toBe('const');
    expect(block?.querySelector('.cx-code-string')?.textContent).toBe('"yes"');
    expect(block?.querySelector('pre code')?.textContent).toBe(code);
  });

  it('keeps unknown-language snippets literal and exposes a copy action', () => {
    const source = '<script>alert("safe")</script>\n  indented();';
    const { container } = render(<RichText text={`\`\`\`customlang\n${source}\n\`\`\``} />);
    expect(container.querySelector('script')).toBeNull();
    expect(container.querySelector('.cx-code-block')?.textContent).toContain(source);
    expect(container.querySelector('.cx-code-keyword, .cx-code-string, .cx-code-number')).toBeNull();
    expect(screen.getByRole('button', { name: 'Copy code' })).toBeInTheDocument();
  });

  it('copies the exact fenced snippet text', async () => {
    const user = userEvent.setup();
    render(<RichText text={'```text\n  keep spaces\nline two\n```'} />);
    await user.click(screen.getByRole('button', { name: 'Copy code' }));
    await waitFor(async () => expect(await navigator.clipboard.readText()).toBe('  keep spaces\nline two'));
    expect(screen.getByRole('button', { name: 'Copied' })).toBeInTheDocument();
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

function FormattingHarness({ initialValue = 'hello\nworld' }: { initialValue?: string }) {
  const textarea = useRef<HTMLTextAreaElement>(null);
  const [value, setValue] = useState(initialValue);
  const formatting = useFormatting(textarea, value, setValue);
  useEffect(() => textarea.current?.setSelectionRange(6, 11), []);
  return (
    <>
      {formatting.toolbar}
      {formatting.richTextButton}
      {formatting.codeDialog}
      {formatting.richTextDialog}
      <textarea ref={textarea} aria-label="Message" value={value} onChange={(event) => setValue(event.target.value)}
        onKeyDown={(event) => formatting.onKeyDown(event)} />
    </>
  );
}

describe('code block composer', () => {
  it('keeps the plain composer toolbar to the code block action', async () => {
    const user = userEvent.setup();
    render(<FormattingHarness />);
    const toolbar = screen.getByRole('toolbar', { name: 'Formatting' });
    expect(within(toolbar).getAllByRole('button')).toHaveLength(1);
    expect(within(toolbar).getByRole('button', { name: 'Code block' })).toBeInTheDocument();
    fireEvent.keyDown(screen.getByLabelText('Message'), { key: 'b', ctrlKey: true });
    expect(screen.getByLabelText('Message')).toHaveValue('hello\nworld');
    const richTextButton = screen.getByRole('button', { name: 'Rich text editor' });
    expect(richTextButton.querySelector('svg')).not.toBeNull();
    expect(richTextButton.querySelector('span:not(.visually-hidden)')).toBeNull();

    await user.click(screen.getByRole('button', { name: 'Rich text editor' }));
    const dialog = screen.getByRole('dialog', { name: 'Rich text editor' });
    expect(within(dialog).getByRole('toolbar', { name: 'Formatting' })).toContainElement(
      within(dialog).getByRole('button', { name: 'Bold' }),
    );
  });

  it('inserts the selected language and code while preserving surrounding text', async () => {
    const user = userEvent.setup();
    render(<FormattingHarness />);
    await user.click(screen.getByRole('button', { name: 'Code block' }));
    await user.selectOptions(screen.getByLabelText('Language'), 'python');
    await user.type(screen.getByRole('textbox', { name: 'Code' }), 'print("hello")');
    await user.click(screen.getByRole('button', { name: 'Insert code' }));
    expect(screen.getByLabelText('Message')).toHaveValue('hello\n```python\nprint("hello")\n```');
  });

  it('edits formatted content visually without exposing markers and applies the source back to the composer', async () => {
    const user = userEvent.setup();
    render(<FormattingHarness initialValue="hello world" />);
    await user.click(screen.getByRole('button', { name: 'Rich text editor' }));
    const dialog = screen.getByRole('dialog', { name: 'Rich text editor' });
    const editor = within(dialog).getByRole('textbox', { name: 'Message content' });
    editor.innerHTML = '<p>hello <strong>world</strong></p>';
    fireEvent.input(editor);
    expect(editor.querySelector('strong')?.textContent).toBe('world');
    expect(editor.textContent).not.toContain('**');
    await user.click(within(dialog).getByRole('button', { name: 'Preview' }));
    expect(within(dialog).getByText('world', { selector: 'strong' })).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Apply to message' }));
    expect(screen.getByRole('textbox', { name: 'Message' })).toHaveValue('hello **world**');
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
