import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, navigate, useLocation } from '../../app/router';
import { currentLanguage } from '../../i18n/i18n';
import { Field, FormAlert } from '../../shared/components/Field';
import { NavIcon } from '../../shared/components/NavIcon';
import { ApiError } from '../../shared/api/httpClient';
import { Captcha } from '../captcha/Captcha';
import { useAuth } from './AuthContext';
import * as api from './authApi';
import { errorMessage, fieldErrors } from './errors';

/** Sign-in, registration, recovery and verification screens. */

/**
 * The account pages' card (redesign 2026-11, owner mockup): a dark welcome panel beside the form
 * on wide screens; on phones only the form.
 */
function AuthCard({ title, subtitle, children, footer }: { title: string; subtitle?: string; children: ReactNode; footer?: ReactNode }) {
  return (
    <div className="cx-auth-split">
      <WelcomePanel />
      <section className="cx-auth-form" aria-labelledby="page-title">
        <span className="cx-auth-icon" aria-hidden="true"><NavIcon name="message-dots" size={44} /></span>
        <h1 id="page-title" className="cx-auth-title">{title}</h1>
        {subtitle && <p className="cx-auth-subtitle">{subtitle}</p>}
        {children}
        {footer && <div className="cx-auth-footer">{footer}</div>}
      </section>
    </div>
  );
}

function WelcomePanel() {
  const { t } = useTranslation();
  return (
    <aside className="cx-auth-panel" aria-hidden="true">
      <span className="cx-auth-arc is-top" />
      <span className="cx-auth-arc is-bottom" />
      <p className="cx-auth-panel-title">
        {t('auth.panel.title1')}<br />{t('auth.panel.titleTo')} <span>{t('auth.panel.title2')}</span>
      </p>
      <p className="cx-auth-panel-text">{t('auth.panel.text')}</p>
      <div className="cx-auth-bubbles">
        <svg className="cx-auth-loop" viewBox="0 0 420 200" focusable="false">
          <path d="M330 40c60 10 70 80 20 95S120 150 80 120 40 40 90 45" fill="none" stroke="currentColor" strokeWidth="3" />
        </svg>
        <div className="cx-auth-bubble is-a">
          <span className="cx-auth-bubble-avatar">A</span>
          <span>{t('auth.panel.bubbleA')}</span>
          <time>10:24</time>
        </div>
        <div className="cx-auth-bubble is-b">
          <span className="cx-auth-bubble-avatar">B</span>
          <span>{t('auth.panel.bubbleB')}</span>
          <time>10:25</time>
        </div>
      </div>
      <p className="cx-auth-panel-foot">
        <NavIcon name="message" size={20} /> {t('auth.panel.conversations')}
        <span>·</span>
        <NavIcon name="layers" size={20} /> {t('auth.panel.channels')}
        <span>·</span>
        <NavIcon name="people" size={20} /> {t('auth.panel.collaboration')}
      </p>
    </aside>
  );
}

/** A password field with a show/hide button (the field stays a password field for managers and screen readers until shown). */
function PasswordField({ label, value, onChange, autoComplete, placeholder, errors }: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  autoComplete: string;
  placeholder?: string;
  errors?: readonly string[];
}) {
  const { t } = useTranslation();
  const id = useId();
  const [shown, setShown] = useState(false);
  const invalid = (errors?.length ?? 0) > 0;
  return (
    <div className="mb-3">
      <label htmlFor={id} className="form-label">{label}</label>
      <div className="cx-password">
        <input id={id} className={`form-control${invalid ? ' is-invalid' : ''}`} type={shown ? 'text' : 'password'} autoComplete={autoComplete}
          required value={value} placeholder={placeholder} onChange={(e) => onChange(e.target.value)} aria-invalid={invalid || undefined}
          aria-describedby={invalid ? `${id}-error` : undefined} />
        <button type="button" className="cx-password-toggle" onClick={() => setShown((s) => !s)} aria-pressed={shown}
          aria-label={shown ? t('auth.hidePassword') : t('auth.showPassword')} aria-controls={id}>
          <NavIcon name={shown ? 'eye-off' : 'eye'} size={20} />
        </button>
      </div>
      {invalid && <div id={`${id}-error`} className="invalid-feedback d-block">{errors!.join(' ')}</div>}
    </div>
  );
}

function ApiUnavailable() {
  const { t } = useTranslation();
  return <FormAlert tone="danger">{t('errors.network')}</FormAlert>;
}

/** Captcha token state that resets after every submission (tokens are single-use). */
function useCaptchaToken() {
  const [token, setToken] = useState<string | null>(null);
  const [resetKey, setResetKey] = useState(0);
  return { token, setToken, resetKey, reset: () => setResetKey((k) => k + 1) };
}

export function SignInPage() {
  const { t } = useTranslation();
  const { query } = useLocation();
  const { config, setUser } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const externalError = query.get('error');
  const returnUrl = safeReturnUrl(query.get('returnUrl'));

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      setUser(await api.login(email.trim(), password, rememberMe));
      navigate(returnUrl, { replace: true });
    } catch (err) {
      setError(err);
      setPassword('');
    } finally {
      setBusy(false);
    }
  };

  const unconfirmed = error instanceof ApiError && error.code === 'email_not_confirmed';

  return (
    <AuthCard
      title={t('auth.signIn.welcome')}
      subtitle={t('auth.signIn.subtitle')}
      footer={
        config?.registrationEnabled && (
          <span>
            {t('auth.signIn.noAccount')} <Link to="/register">{t('auth.signIn.createAccount')}</Link>
          </span>
        )
      }
    >
      {query.get('accountDeleted') === '1' && !error && <FormAlert tone="success">{t('account.deletion.done')}</FormAlert>}
      {externalError && !error && <FormAlert tone="danger">{t(`errors.${externalError}`, { defaultValue: t('errors.unexpected') })}</FormAlert>}
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {unconfirmed && (
        <p>
          <Link to="/resend-confirmation">{t('auth.signIn.resend')}</Link>
        </p>
      )}
      <form onSubmit={submit} noValidate>
        <Field label={t('auth.fields.email')} type="email" autoComplete="username" required value={email} placeholder={t('auth.signIn.emailPlaceholder')}
          onChange={(e) => setEmail(e.target.value)} />
        <PasswordField label={t('auth.fields.password')} autoComplete="current-password" value={password} onChange={setPassword}
          placeholder={t('auth.signIn.passwordPlaceholder')} />
        <div className="form-check mb-3">
          <input id="remember-me" className="form-check-input" type="checkbox" checked={rememberMe} onChange={(e) => setRememberMe(e.target.checked)} />
          <label htmlFor="remember-me" className="form-check-label">
            {t('auth.signIn.rememberMe')}
          </label>
        </div>
        <button type="submit" className="btn btn-primary btn-lg w-100 cx-icon-button justify-content-center" disabled={busy || !email || !password}>
          {busy ? t('auth.working') : t('auth.signIn.submit')}
          {!busy && <NavIcon name="arrow-right" size={20} />}
        </button>
        <p className="text-center mt-3 mb-0">
          <Link to="/forgot-password">{t('auth.signIn.forgot')}</Link>
        </p>
      </form>
      {config && config.externalProviders.length > 0 && (
        <div className="cx-external">
          <p className="cx-divider">
            <span>{t('auth.signIn.or')}</span>
          </p>
          {config.externalProviders.map((provider) => (
            <a key={provider} className="btn btn-outline-secondary w-100 mb-2" href={api.externalSignInUrl(provider, returnUrl)}>
              {t(`auth.external.${provider}`)}
            </a>
          ))}
        </div>
      )}
    </AuthCard>
  );
}

export function RegisterPage() {
  const { t } = useTranslation();
  const { config, status } = useAuth();
  const captcha = useCaptchaToken();
  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [done, setDone] = useState(false);

  if (status === 'ready' && !config) return <AuthCard title={t('auth.register.title')}><ApiUnavailable /></AuthCard>;
  if (config && !config.registrationEnabled) {
    return (
      <AuthCard title={t('auth.register.title')}>
        <FormAlert tone="info">{t('errors.registration_disabled')}</FormAlert>
      </AuthCard>
    );
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.register({ email: email.trim(), password, displayName, language: currentLanguage(), captchaToken: captcha.token });
      setDone(true);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
      captcha.reset();
    }
  };

  if (done && config?.autoConfirmEmail) {
    return (
      <AuthCard title={t('auth.register.readyTitle')} footer={<Link to="/sign-in">{t('nav.signIn')}</Link>}>
        <FormAlert tone="success">{t('auth.register.ready', { email })}</FormAlert>
      </AuthCard>
    );
  }

  if (done) {
    return (
      <AuthCard title={t('auth.register.checkEmailTitle')} footer={<Link to="/sign-in">{t('nav.signIn')}</Link>}>
        <FormAlert tone="success">{t('auth.register.checkEmail', { email })}</FormAlert>
      </AuthCard>
    );
  }

  const isValidation = error instanceof ApiError && error.code === 'validation_failed';
  return (
    <AuthCard
      title={t('auth.register.title')}
      footer={
        <span>
          {t('auth.register.haveAccount')} <Link to="/sign-in">{t('nav.signIn')}</Link>
        </span>
      }
    >
      {error !== null && !isValidation && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <form onSubmit={submit} noValidate>
        <Field
          label={t('auth.fields.displayName')}
          autoComplete="name"
          required
          maxLength={80}
          value={displayName}
          onChange={(e) => setDisplayName(e.target.value)}
          errors={fieldErrors(error, 'displayName', t)}
        />
        <Field
          label={t('auth.fields.email')}
          type="email"
          autoComplete="email"
          required
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          errors={fieldErrors(error, 'email', t)}
        />
        <Field
          label={t('auth.fields.password')}
          type="password"
          autoComplete="new-password"
          required
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          hint={config ? t('auth.passwordHint', { count: config.passwordMinimumLength }) : undefined}
          errors={fieldErrors(error, 'password', t)}
        />
        {config && (
          <Captcha provider={config.captcha.provider} siteKey={config.captcha.siteKey} action="register" onToken={captcha.setToken} resetKey={captcha.resetKey} />
        )}
        <button type="submit" className="btn btn-primary w-100" disabled={busy || !captcha.token || !email || !password || !displayName}>
          {busy ? t('auth.working') : t('auth.register.submit')}
        </button>
      </form>
    </AuthCard>
  );
}

/** Shared form for "email me a link" operations that never reveal whether the account exists. */
function EmailLinkPage({ kind }: { kind: 'forgot' | 'resend' }) {
  const { t } = useTranslation();
  const { config, status } = useAuth();
  const captcha = useCaptchaToken();
  const [email, setEmail] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [sent, setSent] = useState(false);
  const texts = kind === 'forgot' ? 'auth.forgot' : 'auth.resend';

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      if (kind === 'forgot') await api.forgotPassword(email.trim(), captcha.token);
      else await api.resendConfirmation(email.trim(), captcha.token);
      setSent(true);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
      captcha.reset();
    }
  };

  return (
    <AuthCard title={t(`${texts}.title`)} footer={<Link to="/sign-in">{t('auth.backToSignIn')}</Link>}>
      {status === 'ready' && !config && <ApiUnavailable />}
      {sent ? (
        <FormAlert tone="success">{t(`${texts}.sent`)}</FormAlert>
      ) : (
        <>
          <p className="cx-muted">{t(`${texts}.intro`)}</p>
          {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
          <form onSubmit={submit} noValidate>
            <Field
              label={t('auth.fields.email')}
              type="email"
              autoComplete="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              errors={fieldErrors(error, 'email', t)}
            />
            {config && (
              <Captcha
                provider={config.captcha.provider}
                siteKey={config.captcha.siteKey}
                action={kind === 'forgot' ? 'forgot_password' : 'resend_confirmation'}
                onToken={captcha.setToken}
                resetKey={captcha.resetKey}
              />
            )}
            <button type="submit" className="btn btn-primary w-100" disabled={busy || !email || !captcha.token}>
              {busy ? t('auth.working') : t(`${texts}.submit`)}
            </button>
          </form>
        </>
      )}
    </AuthCard>
  );
}

export const ForgotPasswordPage = () => <EmailLinkPage kind="forgot" />;
export const ResendConfirmationPage = () => <EmailLinkPage kind="resend" />;

export function ResetPasswordPage() {
  const { t } = useTranslation();
  const { query } = useLocation();
  const { config } = useAuth();
  const userId = query.get('user') ?? '';
  const token = query.get('token') ?? '';
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [done, setDone] = useState(false);
  const mismatch = confirm.length > 0 && confirm !== password;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (mismatch) return;
    setBusy(true);
    setError(null);
    try {
      await api.resetPassword(userId, token, password);
      setDone(true);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  if (!userId || !token) {
    return (
      <AuthCard title={t('auth.reset.title')} footer={<Link to="/forgot-password">{t('auth.reset.requestNew')}</Link>}>
        <FormAlert tone="danger">{t('errors.invalid_or_expired_token')}</FormAlert>
      </AuthCard>
    );
  }

  if (done) {
    return (
      <AuthCard title={t('auth.reset.title')} footer={<Link to="/sign-in">{t('nav.signIn')}</Link>}>
        <FormAlert tone="success">{t('auth.reset.done')}</FormAlert>
      </AuthCard>
    );
  }

  const isValidation = error instanceof ApiError && error.code === 'validation_failed';
  return (
    <AuthCard title={t('auth.reset.title')} footer={<Link to="/forgot-password">{t('auth.reset.requestNew')}</Link>}>
      {error !== null && !isValidation && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <form onSubmit={submit} noValidate>
        <Field
          label={t('auth.fields.newPassword')}
          type="password"
          autoComplete="new-password"
          required
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          hint={config ? t('auth.passwordHint', { count: config.passwordMinimumLength }) : undefined}
          errors={fieldErrors(error, 'newPassword', t)}
        />
        <Field
          label={t('auth.fields.confirmPassword')}
          type="password"
          autoComplete="new-password"
          required
          value={confirm}
          onChange={(e) => setConfirm(e.target.value)}
          errors={mismatch ? [t('fieldErrors.password_mismatch')] : []}
        />
        <button type="submit" className="btn btn-primary w-100" disabled={busy || !password || mismatch || !confirm}>
          {busy ? t('auth.working') : t('auth.reset.submit')}
        </button>
      </form>
    </AuthCard>
  );
}

export function VerifyEmailPage() {
  const { t } = useTranslation();
  const { query } = useLocation();
  const userId = query.get('user') ?? '';
  const token = query.get('token') ?? '';
  const [state, setState] = useState<'verifying' | 'done' | 'failed'>(userId && token ? 'verifying' : 'failed');
  const started = useRef(false);

  useEffect(() => {
    // Tokens are single-use: never submit twice (React StrictMode re-runs effects in development).
    if (started.current || !userId || !token) return;
    started.current = true;
    api.confirmEmail(userId, token).then(
      () => setState('done'),
      () => setState('failed'),
    );
  }, [userId, token]);

  return (
    <AuthCard title={t('auth.verify.title')}>
      <div role="status" aria-live="polite">
        {state === 'verifying' && (
          <p className="cx-status-line">
            <span className="spinner-border spinner-border-sm text-primary" aria-hidden="true" /> {t('auth.verify.verifying')}
          </p>
        )}
        {state === 'done' && (
          <>
            <FormAlert tone="success">{t('auth.verify.done')}</FormAlert>
            <Link to="/sign-in" className="btn btn-primary">
              {t('nav.signIn')}
            </Link>
          </>
        )}
        {state === 'failed' && (
          <>
            <FormAlert tone="danger">{t('errors.invalid_or_expired_token')}</FormAlert>
            <Link to="/resend-confirmation">{t('auth.signIn.resend')}</Link>
          </>
        )}
      </div>
    </AuthCard>
  );
}

/** Only same-app paths are accepted as post-sign-in destinations. */
export function safeReturnUrl(value: string | null): string {
  return value && value.startsWith('/') && !value.startsWith('//') && !value.includes('\\') ? value : '/account';
}
