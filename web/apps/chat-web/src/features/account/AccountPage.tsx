import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, navigate, useLocation } from '../../app/router';
import type { AccountDeletionCheckResponse, SessionResponse } from '../../shared/api/contracts';
import { Avatar } from '../../shared/components/Avatar';
import { Field, FormAlert } from '../../shared/components/Field';
import { ApiError } from '../../shared/api/httpClient';
import { useAuth } from '../auth/AuthContext';
import * as api from '../auth/authApi';
import { errorMessage, fieldErrors } from '../auth/errors';
import { LanguageSelector } from '../language/LanguageSelector';
import { desktopNotificationsSupported } from '../notifications/NotificationsContext';
import { currentSubscription, disablePush, enablePush, getPushConfiguration, pushSupported, type PushConfiguration } from '../notifications/pushApi';

/** The signed-in user's profile, password and sessions. Redirects anonymous visitors to sign-in. */
export function AccountPage() {
  const { t } = useTranslation();
  const { user, status, signOut } = useAuth();
  const { pathname } = useLocation();

  useEffect(() => {
    // Only while still on this page: after deleting the account the page has already moved on.
    if (status === 'ready' && !user && pathname === '/account') navigate('/sign-in?returnUrl=%2Faccount', { replace: true });
  }, [status, user, pathname]);

  if (!user) {
    return (
      <p className="cx-status-line" role="status">
        <span className="spinner-border spinner-border-sm text-primary" aria-hidden="true" /> {t('auth.working')}
      </p>
    );
  }

  return (
    <>
      <div className="cx-account-header">
        <Avatar user={user} />
        <div>
          <h1 className="cx-page-title mb-0">{user.displayName}</h1>
          <p className="cx-muted mb-0">
            {user.email}
            {user.emailConfirmed && <span className="badge text-bg-success ms-2">{t('account.verified')}</span>}
          </p>
        </div>
        <button
          type="button"
          className="btn btn-outline-secondary ms-auto"
          onClick={() => void signOut().then(() => navigate('/', { replace: true }))}
        >
          {t('nav.signOut')}
        </button>
      </div>

      <div className="cx-grid">
        <ProfileSection />
        {user.hasPassword ? <PasswordSection /> : <ExternalOnlySection providers={user.externalProviders} />}
      </div>
      <PreferencesSection />
      <SessionsSection />
      <DeleteAccountSection />
    </>
  );
}

/**
 * Account deletion (Phase 13): personal data is erased, messages stay as "Deleted user". Waits
 * while the person is the only owner of an organization or the last administrator.
 */
function DeleteAccountSection() {
  const { t } = useTranslation();
  const { user, setUser } = useAuth();
  const [open, setOpen] = useState(false);
  const [check, setCheck] = useState<AccountDeletionCheckResponse | null>(null);
  const [secret, setSecret] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    if (!open) return;
    setCheck(null);
    api.getAccountDeletionCheck().then(setCheck, setError);
  }, [open]);

  if (!user) return null;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!check) return;
    setBusy(true);
    setError(null);
    try {
      await api.deleteAccount(check.requiresPassword ? { password: secret } : { confirmEmail: secret });
      navigate('/sign-in?accountDeleted=1', { replace: true });
      setUser(null);
    } catch (err) {
      setError(err);
      setSecret('');
    } finally {
      setBusy(false);
    }
  };

  const field = check?.requiresPassword ? 'password' : 'confirmEmail';
  const isValidation = error instanceof ApiError && error.code === 'validation_failed';
  return (
    <section className="cx-card cx-card-danger mt-3" aria-labelledby="delete-account-title">
      <div className="cx-card-header">
        <h2 id="delete-account-title" className="cx-card-title">
          {t('account.deletion.title')}
        </h2>
        {!open && (
          <button type="button" className="btn btn-sm btn-outline-danger" onClick={() => setOpen(true)}>
            {t('account.deletion.start')}
          </button>
        )}
      </div>
      <p className="cx-muted">{t('account.deletion.summary')}</p>
      {open && error !== null && !isValidation && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {open && check === null && error === null && <p className="cx-muted mb-0">{t('auth.working')}</p>}
      {open && check && !check.canDelete && (
        <div className="alert alert-warning" role="alert">
          {check.lastAdministrator && <p className="mb-2">{t('account.deletion.lastAdministrator')}</p>}
          {check.blockingOrganizations.length > 0 && (
            <>
              <p className="mb-1">{t('account.deletion.soleOwner')}</p>
              <ul className="mb-0">
                {check.blockingOrganizations.map((org) => (
                  <li key={org.organizationId}>
                    <Link to={`/organization?id=${encodeURIComponent(org.organizationId)}`}>{org.name}</Link>
                  </li>
                ))}
              </ul>
            </>
          )}
        </div>
      )}
      {open && check?.canDelete && (
        <form onSubmit={submit} noValidate>
          <ul className="cx-deletion-list">
            <li>{t('account.deletion.erased')}</li>
            <li>{t('account.deletion.kept')}</li>
            <li>{t('account.deletion.final')}</li>
          </ul>
          <Field
            label={check.requiresPassword ? t('account.deletion.password') : t('account.deletion.typeEmail', { email: user.email })}
            type={check.requiresPassword ? 'password' : 'email'}
            autoComplete={check.requiresPassword ? 'current-password' : 'off'}
            value={secret}
            onChange={(e) => setSecret(e.target.value)}
            errors={fieldErrors(error, field, t)}
          />
          <div className="d-flex gap-2">
            <button type="submit" className="btn btn-danger" disabled={busy || secret.trim() === ''}>
              {t('account.deletion.confirm')}
            </button>
            <button type="button" className="btn btn-outline-secondary" onClick={() => { setOpen(false); setSecret(''); setError(null); }}>
              {t('account.deletion.cancel')}
            </button>
          </div>
        </form>
      )}
    </section>
  );
}

/** Theme, language and notifications; saved to the account so they follow the user (Phase 8). */
function PreferencesSection() {
  const { t } = useTranslation();
  const { user, setUser } = useAuth();
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState<string | null>(null);
  if (!user) return null;
  const prefs = user.preferences;
  const theme = prefs.theme ?? 'system';

  const save = async (changes: Parameters<typeof api.savePreferences>[0]) => {
    setError(null);
    try {
      setUser(await api.savePreferences(changes));
    } catch (err) {
      setError(err);
    }
  };

  const toggleDesktop = async (on: boolean) => {
    setNotice(null);
    if (on) {
      if (!desktopNotificationsSupported()) {
        setNotice(t('preferences.desktopUnsupported'));
        return;
      }
      const permission = Notification.permission === 'default' ? await Notification.requestPermission() : Notification.permission;
      if (permission !== 'granted') {
        setNotice(t('preferences.desktopBlocked'));
        return;
      }
    }
    await save({ desktopNotifications: on });
  };

  return (
    <section className="cx-card mt-3" aria-labelledby="preferences-title">
      <h2 id="preferences-title" className="cx-card-title">
        {t('preferences.title')}
      </h2>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {notice && <FormAlert tone="info">{notice}</FormAlert>}
      <div className="cx-preferences">
        <fieldset>
          <legend className="form-label fs-6">{t('preferences.theme')}</legend>
          <div className="btn-group" role="group">
            {(['system', 'light', 'dark'] as const).map((option) => (
              <button
                key={option}
                type="button"
                className={`btn btn-sm ${theme === option ? 'btn-primary' : 'btn-outline-primary'}`}
                aria-pressed={theme === option}
                onClick={() => void save({ theme: option })}
              >
                {t(`preferences.themes.${option}`)}
              </button>
            ))}
          </div>
        </fieldset>
        <div className="cx-preferences-language">
          <LanguageSelector />
        </div>
        <fieldset>
          <legend className="form-label fs-6">{t('preferences.notifications')}</legend>
          <div className="form-check form-switch">
            <input
              id="pref-desktop"
              className="form-check-input"
              type="checkbox"
              role="switch"
              checked={Boolean(prefs.desktopNotifications)}
              onChange={(e) => void toggleDesktop(e.target.checked)}
            />
            <label className="form-check-label" htmlFor="pref-desktop">
              {t('preferences.desktop')}
            </label>
          </div>
          <div className="form-check form-switch">
            <input
              id="pref-previews"
              className="form-check-input"
              type="checkbox"
              role="switch"
              disabled={!prefs.desktopNotifications}
              checked={prefs.notificationPreviews !== false}
              onChange={(e) => void save({ notificationPreviews: e.target.checked })}
            />
            <label className="form-check-label" htmlFor="pref-previews">
              {t('preferences.previews')}
            </label>
          </div>
          <div className="form-text">{t('preferences.notificationsHint')}</div>
        </fieldset>
        <fieldset>
          <legend className="form-label fs-6">{t('preferences.away')}</legend>
          <label className="form-label small mb-1" htmlFor="pref-email">{t('preferences.email')}</label>
          <select
            id="pref-email"
            className="form-select form-select-sm mb-2"
            value={prefs.emailNotifications ?? 'batched'}
            onChange={(e) => void save({ emailNotifications: e.target.value as 'batched' | 'daily' | 'off' })}
          >
            {(['batched', 'daily', 'off'] as const).map((mode) => (
              <option key={mode} value={mode}>{t(`preferences.emailModes.${mode}`)}</option>
            ))}
          </select>
          <PushToggle onError={setError} onNotice={setNotice} />
        </fieldset>
      </div>
    </section>
  );
}

/** Web Push for this browser (Phase 15): notifications while ChatX is closed. */
function PushToggle({ onError, onNotice }: { onError: (error: unknown) => void; onNotice: (notice: string | null) => void }) {
  const { t } = useTranslation();
  const [config, setConfig] = useState<PushConfiguration | null>(null);
  const [on, setOn] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!pushSupported()) return;
    getPushConfiguration().then(setConfig, () => undefined);
    currentSubscription().then((s) => setOn(s !== null), () => undefined);
  }, []);

  if (!pushSupported() || !config?.enabled || !config.publicKey) {
    return <div className="form-text">{t('preferences.pushUnavailable')}</div>;
  }

  const toggle = async (next: boolean) => {
    setBusy(true);
    onError(null);
    onNotice(null);
    try {
      if (next) {
        if ((await enablePush(config.publicKey!)) === 'blocked') {
          onNotice(t('preferences.desktopBlocked'));
          return;
        }
      } else {
        await disablePush();
      }
      setOn(next);
    } catch (err) {
      onError(err);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="form-check form-switch">
      <input id="pref-push" className="form-check-input" type="checkbox" role="switch" checked={on} disabled={busy}
        onChange={(e) => void toggle(e.target.checked)} />
      <label className="form-check-label" htmlFor="pref-push">{t('preferences.push')}</label>
      <div className="form-text">{t('preferences.pushHint')}</div>
    </div>
  );
}

function ProfileSection() {
  const { t } = useTranslation();
  const { user, setUser } = useAuth();
  const [displayName, setDisplayName] = useState(user?.displayName ?? '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [saved, setSaved] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setSaved(false);
    try {
      setUser(await api.updateProfile(displayName));
      setSaved(true);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const errors = error instanceof ApiError && error.code?.startsWith('display_name') ? [errorMessage(error, t)] : fieldErrors(error, 'displayName', t);
  return (
    <section className="cx-card" aria-labelledby="profile-title">
      <h2 id="profile-title" className="cx-card-title">
        {t('account.profile.title')}
      </h2>
      {saved && <FormAlert tone="success">{t('account.saved')}</FormAlert>}
      {error !== null && errors.length === 0 && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <form onSubmit={submit} noValidate>
        <Field
          label={t('auth.fields.displayName')}
          autoComplete="name"
          maxLength={80}
          value={displayName}
          onChange={(e) => setDisplayName(e.target.value)}
          errors={errors}
          hint={t('account.profile.avatarHint')}
        />
        <button type="submit" className="btn btn-primary" disabled={busy || displayName.trim() === user?.displayName}>
          {t('account.save')}
        </button>
      </form>
    </section>
  );
}

function PasswordSection() {
  const { t } = useTranslation();
  const { config } = useAuth();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [saved, setSaved] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setSaved(false);
    try {
      await api.changePassword(current, next);
      setSaved(true);
      setCurrent('');
      setNext('');
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const isValidation = error instanceof ApiError && error.code === 'validation_failed';
  return (
    <section className="cx-card" aria-labelledby="password-title">
      <h2 id="password-title" className="cx-card-title">
        {t('account.password.title')}
      </h2>
      {saved && <FormAlert tone="success">{t('account.password.changed')}</FormAlert>}
      {error !== null && !isValidation && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <form onSubmit={submit} noValidate>
        <Field
          label={t('auth.fields.currentPassword')}
          type="password"
          autoComplete="current-password"
          value={current}
          onChange={(e) => setCurrent(e.target.value)}
          errors={fieldErrors(error, 'currentPassword', t)}
        />
        <Field
          label={t('auth.fields.newPassword')}
          type="password"
          autoComplete="new-password"
          value={next}
          onChange={(e) => setNext(e.target.value)}
          hint={config ? t('auth.passwordHint', { count: config.passwordMinimumLength }) : undefined}
          errors={fieldErrors(error, 'newPassword', t)}
        />
        <button type="submit" className="btn btn-primary" disabled={busy || !current || !next}>
          {t('account.password.submit')}
        </button>
      </form>
    </section>
  );
}

function ExternalOnlySection({ providers }: { providers: string[] }) {
  const { t } = useTranslation();
  return (
    <section className="cx-card" aria-labelledby="password-title">
      <h2 id="password-title" className="cx-card-title">
        {t('account.password.title')}
      </h2>
      <p className="cx-muted mb-0">
        {t('account.password.externalOnly', { providers: providers.map((p) => t(`auth.providerNames.${p}`, { defaultValue: p })).join(', ') })}
      </p>
    </section>
  );
}

function SessionsSection() {
  const { t, i18n } = useTranslation();
  const [sessions, setSessions] = useState<SessionResponse[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const language = i18n.resolvedLanguage ?? 'en';
  const format = new Intl.DateTimeFormat(language, { dateStyle: 'medium', timeStyle: 'short' });

  const load = useCallback(() => {
    api.listSessions().then(setSessions, setError);
  }, []);
  useEffect(load, [load]);

  const act = async (action: () => Promise<unknown>) => {
    setError(null);
    try {
      await action();
    } catch (err) {
      setError(err);
    }
    load();
  };

  const others = sessions?.filter((s) => !s.current) ?? [];
  return (
    <section className="cx-card mt-3" aria-labelledby="sessions-title">
      <div className="cx-card-header">
        <h2 id="sessions-title" className="cx-card-title">
          {t('account.sessions.title')}
        </h2>
        {others.length > 0 && (
          <button type="button" className="btn btn-sm btn-outline-danger" onClick={() => void act(api.revokeOtherSessions)}>
            {t('account.sessions.revokeOthers')}
          </button>
        )}
      </div>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {sessions === null ? (
        <p className="cx-muted mb-0">{t('auth.working')}</p>
      ) : (
        <ul className="list-unstyled mb-0 cx-session-list">
          {sessions.map((session) => (
            <li key={session.id} className="cx-session">
              <div>
                <strong>{session.description}</strong>
                {session.current && <span className="badge text-bg-primary ms-2">{t('account.sessions.current')}</span>}
                <div className="cx-meta">
                  {t('account.sessions.signedIn', { date: format.format(new Date(session.createdAtUtc)) })} ·{' '}
                  {t('account.sessions.lastActive', { date: format.format(new Date(session.lastSeenAtUtc)) })}
                </div>
              </div>
              {!session.current && (
                <button
                  type="button"
                  className="btn btn-sm btn-outline-secondary"
                  onClick={() => void act(() => api.revokeSession(session.id))}
                  aria-label={t('account.sessions.revokeNamed', { name: session.description })}
                >
                  {t('account.sessions.revoke')}
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
