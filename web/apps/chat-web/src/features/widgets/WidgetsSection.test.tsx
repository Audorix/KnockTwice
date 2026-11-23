import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { resetCsrfToken } from '../../shared/api/httpClient';
import { json, mockFetch } from '../../test/fetchRoutes';
import { WidgetsSection } from './WidgetsSection';

const org = 'o-1';
const ana = { id: 'u-ana', displayName: 'Ana', avatar: { initials: 'A' } };
const ben = { id: 'u-ben', displayName: 'Ben', avatar: { initials: 'B' } };
const widget = {
  id: 'w-1', organizationId: org, name: 'Shop', title: 'Shop', welcomeMessage: null, accentColor: '#0b6e66', position: 'right', defaultLanguage: 'auto',
  allowAnonymous: true, enabled: false, allowedOrigins: [], responders: [ana], createdAtUtc: '2026-11-23T10:00:00Z', updatedAtUtc: '2026-11-23T10:00:00Z',
};

describe('WidgetsSection', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    resetCsrfToken();
    await i18n.changeLanguage('en');
  });

  it('creates a widget, shows its secret once and saves its settings', async () => {
    let saved: Record<string, unknown> | null = null;
    mockFetch({
      'GET /api/v1/auth/csrf': () => json(200, { token: 't', headerName: 'X-CSRF-TOKEN' }),
      [`GET /api/v1/organizations/${org}/widgets`]: () => json(200, []),
      [`POST /api/v1/organizations/${org}/widgets`]: () => json(201, { widget, signingSecret: 's3cr3t-value' }),
      [`PUT /api/v1/organizations/${org}/widgets/w-1`]: (init) => {
        saved = JSON.parse(String(init?.body)) as Record<string, unknown>;
        return json(200, { ...widget, ...saved, allowedOrigins: ['https://shop.example.org'], responders: [ana, ben] });
      },
    });
    const user = userEvent.setup();
    render(<WidgetsSection organizationId={org} members={[ana, ben]} />);

    expect(await screen.findByText('No website chats yet.')).toBeInTheDocument();
    await user.type(screen.getByLabelText('New website chat'), 'Shop');
    await user.click(screen.getByRole('button', { name: 'Create' }));

    expect(await screen.findByLabelText('Signing secret')).toHaveValue('s3cr3t-value');
    const editor = screen.getByRole('form', { name: 'Settings of Shop' });
    expect(within(editor).getByLabelText('Embed code')).toHaveValue(`<script src="${window.location.origin}/widget.js" data-widget-id="w-1" async></script>`);

    await user.type(within(editor).getByLabelText('Website addresses allowed to show the chat'), 'https://shop.example.org');
    await user.click(within(editor).getByLabelText('Ben'));
    await user.click(within(editor).getByLabelText('Chat is on'));
    await user.click(within(editor).getByRole('button', { name: 'Save' }));

    expect(await within(editor).findByText('Saved.')).toBeInTheDocument();
    expect(saved).toMatchObject({ allowedOrigins: ['https://shop.example.org'], responderIds: ['u-ana', 'u-ben'], enabled: true, allowAnonymous: true });

    await user.click(screen.getByRole('button', { name: 'I have stored it' }));
    expect(screen.queryByLabelText('Signing secret')).not.toBeInTheDocument();
  });
});
