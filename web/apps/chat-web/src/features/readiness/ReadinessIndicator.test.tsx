import { beforeAll, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { initI18n } from '../../i18n/i18n';
import i18n from '../../i18n/i18n';
import { ReadinessIndicator } from './ReadinessIndicator';

function healthy(): Response {
  return new Response(JSON.stringify({ status: 'healthy', checks: [] }), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  });
}

describe('ReadinessIndicator', () => {
  beforeAll(async () => {
    await initI18n();
    await i18n.changeLanguage('en');
  });

  it('shows loading, then the unavailable state when the API cannot be reached', async () => {
    let fail!: (reason: unknown) => void;
    vi.spyOn(globalThis, 'fetch').mockReturnValueOnce(new Promise<Response>((_, reject) => (fail = reject)));

    render(<ReadinessIndicator />);
    expect(screen.getByRole('status')).toHaveTextContent('Checking the API…');

    fail(new TypeError('Failed to fetch'));
    expect(await screen.findByText('API unavailable')).toBeInTheDocument();
  });

  it('re-checks from the keyboard and reports recovery', async () => {
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValueOnce(healthy());
    const user = userEvent.setup();

    render(<ReadinessIndicator />);
    await screen.findByText('API unavailable');

    await user.tab();
    expect(screen.getByRole('button', { name: 'Check again' })).toHaveFocus();
    await user.keyboard('{Enter}');

    expect(await screen.findByText('API ready')).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});
