import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import i18n, { initI18n } from '../../i18n/i18n';
import { header, json, mockFetch } from '../../test/fetchRoutes';
import { SetupGate } from './SetupGate';

const configuration = {
  environment: 'Development',
  database: {
    locked: false,
    provider: null,
    supportedProviders: ['Sqlite', 'PostgreSql'],
    sqlitePath: 'App_Data/chatx.db',
    postgreSqlHost: 'localhost',
    postgreSqlPort: 5432,
    postgreSqlDatabase: 'chatx',
  },
  email: {
    provider: 'Smtp',
    fromAddress: 'no-reply@chatx.localhost',
    fromName: 'Knock Twice',
    smtpHost: 'localhost',
    smtpPort: 1025,
    smtpSecurity: 'None',
    smtpSecurityModes: ['None', 'StartTls', 'StartTlsWhenAvailable', 'SslOnConnect', 'Auto'],
  },
  options: {
    developmentMode: true,
    publicBaseUrl: 'http://localhost:5173',
    defaultLanguage: 'en',
    supportedLanguages: ['en', 'es'],
    captchaProvider: 'Turnstile',
    captchaKeysConfigured: true,
  },
  passwordMinimumLength: 10,
};

describe('SetupWizard', () => {
  beforeAll(async () => {
    await initI18n();
  });

  beforeEach(async () => {
    window.history.replaceState(null, '', '/');
    await i18n.changeLanguage('en');
  });

  it('renders the application when setup is not required', async () => {
    mockFetch({ 'GET /api/v1/setup/status': () => json(200, { required: false, completed: true }) });
    render(<SetupGate><p>application</p></SetupGate>);
    expect(await screen.findByText('application')).toBeInTheDocument();
  });

  it('rejects a wrong setup code with a localized message', async () => {
    mockFetch({
      'GET /api/v1/setup/status': () => json(200, { required: true, completed: false }),
      'POST /api/v1/setup/token': () => json(401, { code: 'setup_token_invalid' }),
    });
    render(<SetupGate><p>application</p></SetupGate>);

    await userEvent.type(await screen.findByLabelText('Setup code'), 'WRONG');
    await userEvent.click(screen.getByRole('button', { name: 'Start setup' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('The setup code is incorrect');
    expect(screen.queryByText('application')).not.toBeInTheDocument();
  });

  it('walks through every step and sends the collected settings with the setup code', async () => {
    let completed: unknown = null;
    let tokenOnComplete: string | undefined;
    mockFetch({
      'GET /api/v1/setup/status': () => json(200, { required: true, completed: false }),
      'POST /api/v1/setup/token': () => new Response(null, { status: 204 }),
      'GET /api/v1/setup/configuration': () => json(200, configuration),
      'POST /api/v1/setup/database/test': () => json(200, { databaseExists: false, result: 'will_be_created' }),
      'POST /api/v1/setup/complete': (init) => {
        completed = JSON.parse(String(init?.body));
        tokenOnComplete = header(init, 'X-Setup-Token');
        return json(200, { administratorEmail: 'root@example.org', restarting: false });
      },
    });
    const user = userEvent.setup();
    render(<SetupGate><p>application</p></SetupGate>);

    await user.type(await screen.findByLabelText('Setup code'), 'ABCD-EFGH-JKLM');
    await user.click(screen.getByRole('button', { name: 'Start setup' }));

    // Database: SQLite default, test the connection.
    expect(await screen.findByRole('heading', { name: 'Database' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Test connection' }));
    expect(await screen.findByText(/The database will be created during setup/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Continue' }));

    // Email: defaults point at a local SMTP server.
    expect(await screen.findByRole('heading', { name: 'Email' })).toBeInTheDocument();
    expect(screen.getByLabelText('SMTP host')).toHaveValue('localhost');
    await user.click(screen.getByRole('button', { name: 'Continue' }));

    // Administrator: mismatching confirmation is caught before continuing.
    expect(await screen.findByRole('heading', { name: 'Administrator account' })).toBeInTheDocument();
    await user.type(screen.getByLabelText('Display name'), 'Root Admin');
    await user.type(screen.getByLabelText('Email address'), 'root@example.org');
    await user.type(screen.getByLabelText('Password'), 'correct horse battery');
    await user.type(screen.getByLabelText('Confirm password'), 'different');
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(await screen.findByText('The passwords do not match.')).toBeInTheDocument();
    await user.clear(screen.getByLabelText('Confirm password'));
    await user.type(screen.getByLabelText('Confirm password'), 'correct horse battery');
    await user.click(screen.getByRole('button', { name: 'Continue' }));

    // Options: development mode is preselected in a Development environment.
    expect(await screen.findByRole('heading', { name: 'Options' })).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: 'Development mode' })).toBeChecked();
    await user.click(screen.getByRole('button', { name: 'Continue' }));

    expect(await screen.findByRole('heading', { name: 'Review and install' })).toBeInTheDocument();
    expect(screen.getByText('Development (no email confirmation)')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Install' }));

    expect(await screen.findByRole('heading', { name: 'Setup complete' })).toBeInTheDocument();
    expect(tokenOnComplete).toBe('ABCD-EFGH-JKLM');
    expect(completed).toMatchObject({
      database: { provider: 'Sqlite', sqlite: { path: 'App_Data/chatx.db' } },
      email: { provider: 'Smtp', smtp: { host: 'localhost', port: 1025, security: 'None' } },
      administrator: { email: 'root@example.org', displayName: 'Root Admin', password: 'correct horse battery' },
      options: { developmentMode: true, publicBaseUrl: 'http://localhost:5173', captchaProvider: 'Turnstile' },
    });
  });

  it('returns to the step that the server rejected', async () => {
    mockFetch({
      'GET /api/v1/setup/status': () => json(200, { required: true, completed: false }),
      'POST /api/v1/setup/token': () => new Response(null, { status: 204 }),
      'GET /api/v1/setup/configuration': () => json(200, { ...configuration, database: { ...configuration.database, locked: true, provider: 'PostgreSql' } }),
      'POST /api/v1/setup/complete': () => json(400, { code: 'setup_database_unavailable', errors: { database: ['authentication_failed'] } }),
    });
    const user = userEvent.setup();
    render(<SetupGate><p>application</p></SetupGate>);

    await user.type(await screen.findByLabelText('Setup code'), 'ABCD');
    await user.click(screen.getByRole('button', { name: 'Start setup' }));
    expect(await screen.findByText(/configured by environment variables/)).toBeInTheDocument();
    for (const step of ['Database', 'Email']) {
      expect(await screen.findByRole('heading', { name: step === 'Database' ? 'Database' : 'Email' })).toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: 'Continue' }));
    }
    await user.type(await screen.findByLabelText('Display name'), 'Root');
    await user.type(screen.getByLabelText('Email address'), 'root@example.org');
    await user.type(screen.getByLabelText('Password'), 'correct horse battery');
    await user.type(screen.getByLabelText('Confirm password'), 'correct horse battery');
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await user.click(await screen.findByRole('button', { name: 'Continue' }));
    await user.click(await screen.findByRole('button', { name: 'Install' }));

    expect(await screen.findByRole('heading', { name: 'Database' })).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('The server rejected the user name or password.');
  });
});
