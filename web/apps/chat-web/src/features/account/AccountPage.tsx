import { lazy, Suspense, useCallback, useEffect, useState, type FormEvent, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, navigate, useLocation } from '../../app/router';
import type { AccountDeletionCheckResponse, SessionResponse } from '../../shared/api/contracts';
import { Avatar } from '../../shared/components/Avatar';
import { Field, FormAlert } from '../../shared/components/Field';
import { NavIcon } from '../../shared/components/NavIcon';
import { ApiError } from '../../shared/api/httpClient';
import { useAuth } from '../auth/AuthContext';
import * as api from '../auth/authApi';
import { errorMessage, fieldErrors } from '../auth/errors';
import { LanguageSelector } from '../language/LanguageSelector';
import { desktopNotificationsSupported } from '../notifications/NotificationsContext';
import { currentSubscription, disablePush, enablePush, getPushConfiguration, pushSupported, type PushConfiguration } from '../notifications/pushApi';
import { ProfileLinksTab } from './ProfileLinks';

/** The photo editor loads only when opened. */
const ProfilePhotoDialog = lazy(() => import('./ProfilePhotoDialog').then((m) => ({ default: m.ProfilePhotoDialog })));

type Tab = 'profile' | 'links' | 'notifications' | 'security' | 'sessions';
const tabs: Tab[] = ['profile', 'links', 'notifications', 'security', 'sessions'];
const isTab = (value: string | null): value is Tab => tabs.includes(value as Tab);

/**
 * "My profile" (redesigned 2026-11 from the owner's mockups): how you appear, a profile photo,
 * links for your contacts, notifications, security and sessions in tabs. Redirects anonymous
 * visitors to sign-in.
 */
export function AccountPage() {
  const { t } = useTranslation();
  const { user, status, signOut } = useAuth();
  const { pathname, query } = useLocation();
  const tab: Tab = isTab(query.get('tab')) ? (query.get('tab') as Tab) : 'profile';
  const [photoOpen, setPhotoOpen] = useState(false);
  const [sessions, setSessions] = useState<SessionResponse[] | null>(null);
  const [sessionsError, setSessionsError] = useState<unknown>(null);

  useEffect(() => {
    // Only while still on this page: after deleting the account the page has already moved on.
    if (status === 'ready' && !user && pathname === '/account') navigate('/sign-in?returnUrl=%2Faccount', { replace: true });
  }, [status, user, pathname]);

  const loadSessions = useCallback(() => {
    api.listSessions().then(setSessions, setSessionsError);
  }, []);
  // Reloaded when the Sessions tab opens and when you come back to this window, so a sign-in in
  // another browser shows up without reloading the page.
  useEffect(() => {
    if (user) loadSessions();
  }, [user?.id, tab === 'sessions', loadSessions]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (!user) return;
    const refresh = () => {
      if (document.visibilityState === 'visible') loadSessions();
    };
    document.addEventListener('visibilitychange', refresh);
    window.addEventListener('focus', refresh);
    return () => {
      document.removeEventListener('visibilitychange', refresh);
      window.removeEventListener('focus', refresh);
    };
  }, [user?.id, loadSessions]); // eslint-disable-line react-hooks/exhaustive-deps

  if (!user) {
    return (
      <p className="cx-status-line" role="status">
        <span className="spinner-border spinner-border-sm text-primary" aria-hidden="true" /> {t('auth.working')}
      </p>
    );
  }

  const select = (next: Tab) => navigate(next === 'profile' ? '/account' : `/account?tab=${next}`, { replace: true });

  return (
    <div className="cx-profile-page">
      <header className="cx-page-header">
        <div>
          <nav aria-label={t('nav.label')} className="cx-breadcrumb">
            <span>{t('profile.crumbAccount')}</span>
            <span aria-hidden="true">/</span>
            <span aria-current="page">{t('profile.crumb')}</span>
          </nav>
          <h1 className="cx-page-title mb-1">{t('profile.title')}</h1>
          <p className="cx-muted mb-0">{t('profile.subtitle')}</p>
        </div>
        <button type="button" className="btn btn-outline-primary" onClick={() => void signOut().then(() => navigate('/', { replace: true }))}>
          {t('nav.signOut')}
        </button>
      </header>

      <ProfileHero onChangePhoto={() => setPhotoOpen(true)} />

      <div className="cx-tabs" role="tablist" aria-label={t('profile.tabsLabel')}>
        {tabs.map((x) => (
          <button
            key={x}
            id={`profile-tab-${x}`}
            type="button"
            role="tab"
            aria-selected={tab === x}
            aria-controls="profile-panel"
            tabIndex={tab === x ? 0 : -1}
            className={`cx-tab${tab === x ? ' is-active' : ''}`}
            onClick={() => select(x)}
            onKeyDown={(e) => moveTab(e, x, select)}
          >
            {t(`profile.tabs.${x}`)}
            {x === 'sessions' && sessions && <> <span className="cx-tab-count">{sessions.length}</span></>}
          </button>
        ))}
      </div>

      <div role="tabpanel" id="profile-panel" aria-labelledby={`profile-tab-${tab}`} className="cx-profile-panel">
        {tab === 'profile' && <ProfileTab sessions={sessions} onChangePhoto={() => setPhotoOpen(true)} onSessions={() => select('sessions')} />}
        {tab === 'links' && <ProfileLinksTab />}
        {tab === 'notifications' && <NotificationsSection />}
        {tab === 'security' && (
          <div className="cx-profile-grid">
            {user.hasPassword ? <PasswordSection /> : <ExternalOnlySection providers={user.externalProviders} />}
            <DeleteAccountSection />
          </div>
        )}
        {tab === 'sessions' && <SessionsSection sessions={sessions} error={sessionsError} reload={loadSessions} />}
      </div>

      {photoOpen && (
        <Suspense fallback={null}>
          <ProfilePhotoDialog onClose={() => setPhotoOpen(false)} />
        </Suspense>
      )}
    </div>
  );
}

/** The large avatar, name and email, the photo buttons and a preview of how others see you. */
function ProfileHero({ onChangePhoto }: { onChangePhoto: () => void }) {
  const { t } = useTranslation();
  const { user, setUser } = useAuth();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  if (!user) return null;
  const hasPhoto = !!user.avatar.photoUrl;

  const switchToInitials = async () => {
    setBusy(true);
    setError(null);
    try {
      setUser(await api.removePhoto());
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="cx-card cx-profile-hero" aria-label={t('profile.heroLabel')}>
      <div className="cx-profile-hero-main">
        <div className="cx-profile-photo">
          <Avatar user={user} size="xl" />
          <button type="button" className="cx-profile-camera" onClick={onChangePhoto} aria-label={t('profile.photo.change')}>
            <NavIcon name="camera" size={18} />
          </button>
        </div>
        <div className="cx-profile-identity">
          <h2 className="cx-profile-name">{user.displayName}</h2>
          <p className="cx-profile-email">
            <span>{user.email}</span>
            {user.emailConfirmed && (
              <span className="cx-profile-verified">
                <NavIcon name="check" size={14} />
                {t('account.verified')}
              </span>
            )}
          </p>
          <p className="cx-muted mb-2">{t('profile.visible')}</p>
          {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
          <div className="cx-profile-actions">
            <button type="button" className="btn btn-outline-primary" onClick={onChangePhoto}>
              {t('profile.photo.change')}
            </button>
            <button type="button" className="btn btn-outline-secondary" onClick={() => void switchToInitials()} disabled={!hasPhoto || busy}>
              {t('profile.photo.useInitials')}
            </button>
          </div>
        </div>
      </div>
      <div className="cx-profile-appear">
        <h2 className="cx-profile-appear-title">{t('profile.appear')}</h2>
        <div className="cx-profile-appear-card">
          <Avatar user={user} />
          <div>
            <strong>{user.displayName}</strong>
            <div className="cx-muted small">{t('profile.appearHint')}</div>
          </div>
        </div>
      </div>
    </section>
  );
}

/** Display name, email and photo; appearance; a sessions summary. */
function ProfileTab({ sessions, onChangePhoto, onSessions }: { sessions: SessionResponse[] | null; onChangePhoto: () => void; onSessions: () => void }) {
  const { t } = useTranslation();
  const { user, setUser } = useAuth();
  const [displayName, setDisplayName] = useState(user?.displayName ?? '');
  const [handle, setHandle] = useState(user?.handle ?? '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [saved, setSaved] = useState(false);
  if (!user) return null;

  const handleChanged = handle.trim().replace(/^@/, '').toLowerCase() !== (user.handle ?? '');
  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setSaved(false);
    try {
      const updated = await api.updateProfile(displayName, handleChanged ? handle : undefined);
      setUser(updated);
      setHandle(updated.handle ?? '');
      setSaved(true);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  // Shared at once (2026-11-24): contacts see the address in your profile drawer.
  const shareEmail = async (share: boolean) => {
    setError(null);
    try {
      setUser(await api.savePreferences({ shareEmailWithContacts: share }));
    } catch (err) {
      setError(err);
    }
  };

  const handleErrors = fieldErrors(error, 'handle', t);
  const errors = error instanceof ApiError && error.code?.startsWith('display_name') ? [errorMessage(error, t)] : fieldErrors(error, 'displayName', t);
  return (
    <div className="cx-profile-grid">
      <section className="cx-card" aria-labelledby="profile-details-title">
        <h2 id="profile-details-title" className="cx-card-title">{t('profile.details.title')}</h2>
        <p className="cx-muted">{t('profile.details.subtitle')}</p>
        {saved && <FormAlert tone="success">{t('account.saved')}</FormAlert>}
        {error !== null && errors.length === 0 && handleErrors.length === 0 && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
        <form onSubmit={submit} noValidate>
          <Field
            label={t('auth.fields.displayName')}
            autoComplete="name"
            maxLength={80}
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            errors={errors}
            hint={t('profile.details.nameHint')}
          />
          <div className="mb-3">
            <label className="form-label" htmlFor="profile-handle">{t('profile.details.handle')}</label>
            <div className="input-group">
              <span className="input-group-text" aria-hidden="true">@</span>
              <input id="profile-handle" className={`form-control${handleErrors.length > 0 ? ' is-invalid' : ''}`} autoComplete="off" spellCheck={false}
                maxLength={31} value={handle} onChange={(e) => setHandle(e.target.value)} aria-describedby="profile-handle-hint"
                aria-invalid={handleErrors.length > 0 || undefined} />
            </div>
            {handleErrors.length > 0 && <div className="invalid-feedback d-block">{handleErrors.join(' ')}</div>}
            <div id="profile-handle-hint" className="form-text">{t('profile.details.handleHint')}</div>
          </div>
          <div className="mb-3">
            <label className="form-label" htmlFor="profile-email">{t('auth.fields.email')}</label>
            <div className="cx-locked-field">
              <NavIcon name="lock" size={16} />
              <input id="profile-email" className="form-control" value={user.email} readOnly aria-describedby="profile-email-hint" />
            </div>
            <div id="profile-email-hint" className="form-text">
              {t(user.preferences.shareEmailWithContacts ? 'profile.details.emailHintShared' : 'profile.details.emailHint')}
            </div>
            <div className="form-check form-switch mt-2">
              <input id="profile-share-email" className="form-check-input" type="checkbox" role="switch"
                checked={!!user.preferences.shareEmailWithContacts} onChange={(e) => void shareEmail(e.target.checked)} />
              <label className="form-check-label" htmlFor="profile-share-email">{t('profile.details.shareEmail')}</label>
            </div>
          </div>
          <div className="cx-profile-photo-row">
            <Avatar user={user} size="lg" />
            <div className="flex-grow-1">
              <strong>{t('profile.photo.title')}</strong>
              <div className="cx-muted small">{t('profile.photo.hint')}</div>
            </div>
            <button type="button" className="btn btn-outline-secondary btn-sm" onClick={onChangePhoto}>{t('profile.photo.changeShort')}</button>
          </div>
          <button type="submit" className="btn btn-primary" disabled={busy || (displayName.trim() === user.displayName && !handleChanged)}>
            {t('profile.details.save')}
          </button>
        </form>
      </section>

      <div className="cx-profile-stack">
        <AppearanceSection />
        <section className="cx-card" aria-labelledby="profile-sessions-title">
          <h2 id="profile-sessions-title" className="cx-card-title">{t('account.sessions.title')}</h2>
          <p className="cx-muted mb-2">
            {sessions ? t('profile.sessionsSummary', { count: sessions.length }) : t('auth.working')}
          </p>
          {sessions && (
            <button type="button" className="btn btn-link p-0 cx-profile-link-button" onClick={onSessions}>
              {t('profile.viewSessions', { count: sessions.length })}
              <NavIcon name="arrow-right" size={16} />
            </button>
          )}
        </section>
      </div>
    </div>
  );
}

/** Theme cards and the interface language; saved to the account so they follow the user (Phase 8). */
function AppearanceSection() {
  const { t } = useTranslation();
  const { user, setUser } = useAuth();
  const [error, setError] = useState<unknown>(null);
  if (!user) return null;
  const theme = user.preferences.theme ?? 'system';

  const save = async (next: 'system' | 'light' | 'dark') => {
    setError(null);
    try {
      setUser(await api.savePreferences({ theme: next }));
    } catch (err) {
      setError(err);
    }
  };

  return (
    <section className="cx-card" aria-labelledby="appearance-title">
      <h2 id="appearance-title" className="cx-card-title">{t('profile.appearance.title')}</h2>
      <p className="cx-muted">{t('profile.appearance.subtitle')}</p>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <div className="cx-theme-cards" role="group" aria-label={t('preferences.theme')}>
        {(['system', 'light', 'dark'] as const).map((option) => (
          <button key={option} type="button" className={`cx-theme-card is-${option}${theme === option ? ' is-selected' : ''}`} aria-pressed={theme === option}
            onClick={() => void save(option)}>
            <span className="cx-theme-sample" aria-hidden="true">
              <span /><span /><span />
            </span>
            <span className="cx-theme-name">{t(`preferences.themes.${option}`)}</span>
            {theme === option && <span className="cx-theme-check" aria-hidden="true"><NavIcon name="check" size={12} /></span>}
          </button>
        ))}
      </div>
      <div className="cx-preferences-language mt-3">
        <LanguageSelector />
      </div>
    </section>
  );
}

/** Desktop, email and push notifications (Phases 8 and 15). */
function NotificationsSection() {
  const { t } = useTranslation();
  const { user, setUser } = useAuth();
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState<string | null>(null);
  if (!user) return null;
  const prefs = user.preferences;

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
    <div className="cx-profile-grid">
      <section className="cx-card" aria-labelledby="notify-here-title">
        <h2 id="notify-here-title" className="cx-card-title">{t('preferences.notifications')}</h2>
        <p className="cx-muted">{t('preferences.notificationsHint')}</p>
        {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
        {notice && <FormAlert tone="info">{notice}</FormAlert>}
        <div className="form-check form-switch">
          <input id="pref-desktop" className="form-check-input" type="checkbox" role="switch" checked={Boolean(prefs.desktopNotifications)}
            onChange={(e) => void toggleDesktop(e.target.checked)} />
          <label className="form-check-label" htmlFor="pref-desktop">{t('preferences.desktop')}</label>
        </div>
        <div className="form-check form-switch">
          <input id="pref-previews" className="form-check-input" type="checkbox" role="switch" disabled={!prefs.desktopNotifications}
            checked={prefs.notificationPreviews !== false} onChange={(e) => void save({ notificationPreviews: e.target.checked })} />
          <label className="form-check-label" htmlFor="pref-previews">{t('preferences.previews')}</label>
        </div>
      </section>
      <section className="cx-card" aria-labelledby="notify-away-title">
        <h2 id="notify-away-title" className="cx-card-title">{t('preferences.away')}</h2>
        <p className="cx-muted">{t('profile.awayHint')}</p>
        <label className="form-label" htmlFor="pref-email">{t('preferences.email')}</label>
        <select id="pref-email" className="form-select mb-3" value={prefs.emailNotifications ?? 'batched'}
          onChange={(e) => void save({ emailNotifications: e.target.value as 'batched' | 'daily' | 'off' })}>
          {(['batched', 'daily', 'off'] as const).map((mode) => (
            <option key={mode} value={mode}>{t(`preferences.emailModes.${mode}`)}</option>
          ))}
        </select>
        <PushToggle onError={setError} onNotice={setNotice} />
      </section>
    </div>
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
      <h2 id="password-title" className="cx-card-title">{t('account.password.title')}</h2>
      <p className="cx-muted">{t('profile.passwordHint')}</p>
      {saved && <FormAlert tone="success">{t('account.password.changed')}</FormAlert>}
      {error !== null && !isValidation && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <form onSubmit={submit} noValidate>
        <Field label={t('auth.fields.currentPassword')} type="password" autoComplete="current-password" value={current}
          onChange={(e) => setCurrent(e.target.value)} errors={fieldErrors(error, 'currentPassword', t)} />
        <Field label={t('auth.fields.newPassword')} type="password" autoComplete="new-password" value={next} onChange={(e) => setNext(e.target.value)}
          hint={config ? t('auth.passwordHint', { count: config.passwordMinimumLength }) : undefined} errors={fieldErrors(error, 'newPassword', t)} />
        <button type="submit" className="btn btn-primary" disabled={busy || !current || !next}>{t('account.password.submit')}</button>
      </form>
    </section>
  );
}

function ExternalOnlySection({ providers }: { providers: string[] }) {
  const { t } = useTranslation();
  return (
    <section className="cx-card" aria-labelledby="password-title">
      <h2 id="password-title" className="cx-card-title">{t('account.password.title')}</h2>
      <p className="cx-muted mb-0">
        {t('account.password.externalOnly', { providers: providers.map((p) => t(`auth.providerNames.${p}`, { defaultValue: p })).join(', ') })}
      </p>
    </section>
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
    <section className="cx-card cx-card-danger" aria-labelledby="delete-account-title">
      <div className="cx-card-header">
        <h2 id="delete-account-title" className="cx-card-title">{t('account.deletion.title')}</h2>
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
            <button type="submit" className="btn btn-danger" disabled={busy || secret.trim() === ''}>{t('account.deletion.confirm')}</button>
            <button type="button" className="btn btn-outline-secondary" onClick={() => { setOpen(false); setSecret(''); setError(null); }}>
              {t('account.deletion.cancel')}
            </button>
          </div>
        </form>
      )}
    </section>
  );
}

function SessionsSection({ sessions, error: loadError, reload }: { sessions: SessionResponse[] | null; error: unknown; reload: () => void }) {
  const { t, i18n } = useTranslation();
  const [error, setError] = useState<unknown>(null);
  const language = i18n.resolvedLanguage ?? 'en';
  const format = new Intl.DateTimeFormat(language, { dateStyle: 'medium', timeStyle: 'short' });

  const act = async (action: () => Promise<unknown>) => {
    setError(null);
    try {
      await action();
    } catch (err) {
      setError(err);
    }
    reload();
  };

  const shown = error ?? loadError;
  const others = sessions?.filter((s) => !s.current) ?? [];
  return (
    <section className="cx-card" aria-labelledby="sessions-title">
      <div className="cx-card-header">
        <h2 id="sessions-title" className="cx-card-title">{t('account.sessions.title')}</h2>
        {others.length > 0 && (
          <button type="button" className="btn btn-sm btn-outline-danger" onClick={() => void act(api.revokeOtherSessions)}>
            {t('account.sessions.revokeOthers')}
          </button>
        )}
      </div>
      <p className="cx-muted">{t('profile.sessionsHint')}</p>
      {shown !== null && shown !== undefined && <FormAlert tone="danger">{errorMessage(shown, t)}</FormAlert>}
      {sessions === null ? (
        <p className="cx-muted mb-0">{t('auth.working')}</p>
      ) : (
        <ul className="list-unstyled mb-0 cx-session-list">
          {sessions.map((session) => (
            <li key={session.id} className="cx-session">
              <span className="cx-session-icon" aria-hidden="true"><NavIcon name="browser" size={20} /></span>
              <div className="flex-grow-1 min-w-0">
                <strong>{session.description}</strong>
                {session.current && <span className="badge text-bg-primary ms-2">{t('account.sessions.current')}</span>}
                <div className="cx-meta">
                  {t('account.sessions.signedIn', { date: format.format(new Date(session.createdAtUtc)) })} ·{' '}
                  {t('account.sessions.lastActive', { date: format.format(new Date(session.lastSeenAtUtc)) })}
                </div>
              </div>
              {!session.current && (
                <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void act(() => api.revokeSession(session.id))}
                  aria-label={t('account.sessions.revokeNamed', { name: session.description })}>
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

function moveTab(event: KeyboardEvent<HTMLButtonElement>, current: Tab, select: (tab: Tab) => void) {
  const index = tabs.indexOf(current);
  const next =
    event.key === 'ArrowRight' ? tabs[(index + 1) % tabs.length] : event.key === 'ArrowLeft' ? tabs[(index + tabs.length - 1) % tabs.length] : event.key === 'Home' ? tabs[0] : event.key === 'End' ? tabs[tabs.length - 1] : undefined;
  if (!next) return;
  event.preventDefault();
  select(next);
  document.getElementById(`profile-tab-${next}`)?.focus();
}
