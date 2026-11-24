import { beforeAll, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { initI18n } from '../../i18n/i18n';
import { ConfirmDialog } from './ConfirmDialog';

const labels = { keepLabel: 'Cancel', confirmLabel: 'Delete', busyLabel: 'Working', closeLabel: 'Close' };

describe('ConfirmDialog', () => {
  beforeAll(async () => {
    await initI18n();
  });

  it('closes only itself on Escape, not the drawer it was opened from', async () => {
    const drawerKeys = vi.fn();
    const onKeep = vi.fn();
    const user = userEvent.setup();
    render(
      <div onKeyDown={(e) => drawerKeys(e.key)}>
        <ConfirmDialog title="Remove Ana?" text="She leaves." {...labels} onKeep={onKeep} onConfirm={() => undefined} />
      </div>,
    );
    await user.keyboard('{Escape}');
    expect(onKeep).toHaveBeenCalledTimes(1);
    expect(drawerKeys).not.toHaveBeenCalledWith('Escape');
  });

  it('keeps Tab inside the dialog', async () => {
    const user = userEvent.setup();
    render(
      <>
        <button type="button">Behind</button>
        <ConfirmDialog title="Remove Ana?" text="She leaves." {...labels} onKeep={() => undefined} onConfirm={() => undefined} />
      </>,
    );
    expect(screen.getByRole('button', { name: 'Cancel' })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole('button', { name: 'Delete' })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole('button', { name: 'Close' })).toHaveFocus();
    await user.tab({ shift: true });
    expect(screen.getByRole('button', { name: 'Delete' })).toHaveFocus();
    expect(screen.getByRole('button', { name: 'Behind' })).not.toHaveFocus();
  });

  it('accepts the typed name in any case, as the server does', async () => {
    const onConfirm = vi.fn();
    const user = userEvent.setup();
    render(
      <ConfirmDialog title="Delete Acme?" text="Gone for good." {...labels} typeToConfirm="Acme" typeLabel="Type Acme"
        onKeep={() => undefined} onConfirm={onConfirm} />,
    );
    expect(screen.getByRole('button', { name: 'Delete' })).toBeDisabled();
    await user.type(screen.getByLabelText('Type Acme'), 'acme');
    await user.click(screen.getByRole('button', { name: 'Delete' }));
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });
});
