import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { currentLanguage } from '../../i18n/i18n';
import { ApiError, getJson } from '../../shared/api/httpClient';
import type {
  CaptchaProvider,
  DatabaseProvider,
  EmailProvider,
  SetupCompleteRequest,
  SetupConfigurationResponse,
  SetupDatabaseInput,
  SetupEmailInput,
} from '../../shared/api/contracts';
import { BrandMark } from '../../shared/components/BrandMark';
import { Field, FormAlert } from '../../shared/components/Field';
import { errorMessage } from '../auth/errors';
import { LanguageSelector } from '../language/LanguageSelector';
import * as api from './setupApi';

/**
 * First-run setup: setup code → database → email → administrator → options → review.
 * Nothing is saved until "Install"; the API then migrates the database, creates the
 * administrator, writes its settings file and restarts into normal mode.
 */

type StepId = 'code' | 'database' | 'email' | 'administrator' | 'options' | 'review';
const steps: StepId[] = ['code', 'database', 'email', 'administrator', 'options', 'review'];
type Phase = 'wizard' | 'installing' | 'restarting' | 'done' | 'slow';

interface SetupForm {
  dbProvider: DatabaseProvider;
  sqlitePath: string;
  pgHost: string;
  pgPort: string;
  pgDatabase: string;
  pgUsername: string;
  pgPassword: string;
  pgSslMode: string;
  emailProvider: EmailProvider;
  fromAddress: string;
  fromName: string;
  smtpHost: string;
  smtpPort: string;
  smtpSecurity: string;
  smtpUsername: string;
  smtpPassword: string;
  postmarkToken: string;
  postmarkStream: string;
  testRecipient: string;
  adminName: string;
  adminEmail: string;
  adminPassword: string;
  adminConfirm: string;
  developmentMode: boolean;
  publicBaseUrl: string;
  defaultLanguage: string;
  captchaProvider: CaptchaProvider;
  captchaSiteKey: string;
  captchaSecretKey: string;
}

function initialForm(config: SetupConfigurationResponse): SetupForm {
  const language = currentLanguage();
  return {
    dbProvider: config.database.provider ?? 'Sqlite',
    sqlitePath: config.database.sqlitePath,
    pgHost: config.database.postgreSqlHost,
    pgPort: String(config.database.postgreSqlPort),
    pgDatabase: config.database.postgreSqlDatabase,
    pgUsername: '',
    pgPassword: '',
    pgSslMode: 'Prefer',
    emailProvider: config.email.provider,
    fromAddress: config.email.fromAddress,
    fromName: config.email.fromName,
    smtpHost: config.email.smtpHost,
    smtpPort: String(config.email.smtpPort),
    smtpSecurity: config.email.smtpSecurity,
    smtpUsername: '',
    smtpPassword: '',
    postmarkToken: '',
    postmarkStream: 'outbound',
    testRecipient: '',
    adminName: '',
    adminEmail: '',
    adminPassword: '',
    adminConfirm: '',
    developmentMode: config.options.developmentMode,
    publicBaseUrl: config.options.publicBaseUrl ?? window.location.origin,
    defaultLanguage: config.options.supportedLanguages.includes(language) ? language : config.options.defaultLanguage,
    captchaProvider: config.options.captchaProvider,
    captchaSiteKey: '',
    captchaSecretKey: '',
  };
}

function toDatabase(form: SetupForm): SetupDatabaseInput {
  return form.dbProvider === 'Sqlite'
    ? { provider: 'Sqlite', sqlite: { path: form.sqlitePath.trim() } }
    : {
        provider: 'PostgreSql',
        postgreSql: {
          host: form.pgHost.trim(),
          port: Number(form.pgPort),
          database: form.pgDatabase.trim(),
          username: form.pgUsername.trim(),
          password: form.pgPassword,
          sslMode: form.pgSslMode,
        },
      };
}

function toEmail(form: SetupForm): SetupEmailInput {
  const base = { provider: form.emailProvider, fromAddress: form.fromAddress.trim(), fromName: form.fromName.trim() };
  return form.emailProvider === 'Smtp'
    ? {
        ...base,
        smtp: {
          host: form.smtpHost.trim(),
          port: Number(form.smtpPort),
          security: form.smtpSecurity,
          username: form.smtpUsername.trim(),
          password: form.smtpPassword,
        },
      }
    : { ...base, postmark: { serverToken: form.postmarkToken.trim(), messageStream: form.postmarkStream.trim() } };
}

function toRequest(form: SetupForm): SetupCompleteRequest {
  return {
    database: toDatabase(form),
    email: toEmail(form),
    administrator: { email: form.adminEmail.trim(), displayName: form.adminName, password: form.adminPassword },
    options: {
      developmentMode: form.developmentMode,
      publicBaseUrl: form.publicBaseUrl.trim(),
      defaultLanguage: form.defaultLanguage,
      captchaProvider: form.captchaProvider,
      captchaSiteKey: form.captchaSiteKey.trim(),
      captchaSecretKey: form.captchaSecretKey.trim(),
    },
  };
}

/** Client-side checks per step; the server repeats and extends all of them. */
function validateStep(step: StepId, form: SetupForm, locked: boolean): Record<string, string[]> {
  const errors: Record<string, string[]> = {};
  const required = (field: string, value: string) => {
    if (!value.trim()) errors[field] = ['required'];
  };
  const port = (field: string, value: string) => {
    const n = Number(value);
    if (!Number.isInteger(n) || n < 1 || n > 65535) errors[field] = ['port_invalid'];
  };
  if (step === 'database' && !locked) {
    if (form.dbProvider === 'Sqlite') required('database.path', form.sqlitePath);
    else {
      required('database.host', form.pgHost);
      port('database.port', form.pgPort);
      required('database.database', form.pgDatabase);
      required('database.username', form.pgUsername);
    }
  }
  if (step === 'email') {
    required('email.fromAddress', form.fromAddress);
    if (form.emailProvider === 'Smtp') {
      required('email.smtpHost', form.smtpHost);
      port('email.smtpPort', form.smtpPort);
      if (!form.smtpUsername.trim() !== !form.smtpPassword) errors['email.smtpCredentials'] = ['credentials_incomplete'];
    } else required('email.postmarkToken', form.postmarkToken);
  }
  if (step === 'administrator') {
    required('administrator.displayName', form.adminName);
    if (!form.adminEmail.trim()) errors['administrator.email'] = ['email_required'];
    if (!form.adminPassword) errors['administrator.password'] = ['password_required'];
    else if (form.adminPassword !== form.adminConfirm) errors['administrator.confirm'] = ['password_mismatch'];
  }
  if (step === 'options') {
    required('options.publicBaseUrl', form.publicBaseUrl);
    if (form.captchaProvider === 'Turnstile' && !form.captchaSiteKey.trim() !== !form.captchaSecretKey.trim()) {
      errors['options.captchaKeys'] = ['credentials_incomplete'];
    }
  }
  return errors;
}

function stepOfField(field: string): StepId | undefined {
  const section = field.split('.')[0];
  return steps.find((s) => s === section);
}

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** After the restart the normal host answers readiness with 200; the setup host always says 503. */
async function waitForNormalMode(timeoutMs = 90000): Promise<boolean> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    await delay(1000);
    try {
      const ready = await getJson('/health/ready', { acceptStatuses: [503], timeoutMs: 3000 });
      if (ready.status === 200) return true;
    } catch {
      // Restarting: the API is briefly unreachable.
    }
  }
  return false;
}

export function SetupWizard() {
  const { t } = useTranslation();
  const [token, setToken] = useState('');
  const [config, setConfig] = useState<SetupConfigurationResponse | null>(null);
  const [form, setForm] = useState<SetupForm | null>(null);
  const [step, setStep] = useState<StepId>('code');
  const [phase, setPhase] = useState<Phase>('wizard');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [clientErrors, setClientErrors] = useState<Record<string, string[]>>({});
  const [notice, setNotice] = useState<{ tone: 'success' | 'info'; text: string } | null>(null);
  const [adminEmail, setAdminEmail] = useState('');
  const heading = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    document.title = `${t('setup.title')} · ${t('app.documentTitle')}`;
  }, [t]);

  // Move focus to the step heading so keyboard and screen-reader users follow the wizard.
  const firstRender = useRef(true);
  useEffect(() => {
    if (firstRender.current) {
      firstRender.current = false;
      return;
    }
    heading.current?.focus();
  }, [step, phase]);

  const set = <K extends keyof SetupForm>(key: K, value: SetupForm[K]) => setForm((current) => (current ? { ...current, [key]: value } : current));
  const locked = config?.database.locked ?? false;

  const messages = (field: string): string[] => {
    const codes = [...(clientErrors[field] ?? []), ...(error instanceof ApiError ? (error.fieldErrors[field] ?? []) : [])];
    return codes.map((code) => {
      const text = t(`fieldErrors.${code}`);
      return text === `fieldErrors.${code}` ? t('fieldErrors.invalid') : text;
    });
  };

  /** Section-level problems (e.g. "database": ["unreachable"]) and configuration details. */
  const sectionAlert = (section: string): ReactNode => {
    if (!(error instanceof ApiError)) return null;
    const codes = error.fieldErrors[section] ?? [];
    if (codes.length === 0) return null;
    return (
      <div className="alert alert-danger py-2" role="alert">
        {codes.map((code) => (
          <p key={code} className="mb-1">
            {t(`setup.reasons.${code}`, { defaultValue: t('errors.unexpected') })}
          </p>
        ))}
        {error.details.length > 0 && (
          <ul className="mb-0 small">
            {error.details.map((detail) => (
              <li key={detail}>{detail}</li>
            ))}
          </ul>
        )}
      </div>
    );
  };

  const run = async (action: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await action();
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const submitCode = (event: FormEvent) => {
    event.preventDefault();
    void run(async () => {
      const code = token.trim();
      await api.verifySetupCode(code);
      const loaded = await api.getSetupConfiguration(code);
      setConfig(loaded);
      setForm((current) => current ?? initialForm(loaded));
      setStep('database');
    });
  };

  const goTo = (next: StepId) => {
    setError(null);
    setNotice(null);
    setClientErrors({});
    setStep(next);
  };

  const next = (event: FormEvent) => {
    event.preventDefault();
    if (!form) return;
    const found = validateStep(step, form, locked);
    setClientErrors(found);
    if (Object.keys(found).length > 0) return;
    const index = steps.indexOf(step);
    goTo(steps[Math.min(index + 1, steps.length - 1)]!);
  };

  const back = () => goTo(steps[Math.max(steps.indexOf(step) - 1, 1)]!);

  const testDatabase = () => {
    if (!form) return;
    void run(async () => {
      const result = await api.testDatabase(token.trim(), toDatabase(form));
      setNotice({ tone: 'success', text: t(`setup.database.${result.result}`) });
    });
  };

  const testEmail = () => {
    if (!form) return;
    if (!form.testRecipient.trim()) {
      setClientErrors({ 'email.testRecipient': ['email_required'] });
      return;
    }
    void run(async () => {
      await api.sendTestEmail(token.trim(), toEmail(form), form.testRecipient.trim());
      setNotice({ tone: 'success', text: t('setup.email.testSent', { email: form.testRecipient.trim() }) });
    });
  };

  const install = async () => {
    if (!form) return;
    setBusy(true);
    setError(null);
    setPhase('installing');
    try {
      const result = await api.completeSetup(token.trim(), toRequest(form));
      setAdminEmail(result.administratorEmail);
      if (result.restarting) {
        setPhase('restarting');
        setPhase((await waitForNormalMode()) ? 'done' : 'slow');
      } else {
        setPhase('done');
      }
    } catch (err) {
      setError(err);
      setPhase('wizard');
      const fields = err instanceof ApiError ? Object.keys(err.fieldErrors) : [];
      const target =
        (fields.map(stepOfField).find(Boolean) as StepId | undefined) ??
        (err instanceof ApiError && err.code === 'setup_administrator_exists' ? 'administrator' : undefined) ??
        (err instanceof ApiError && err.code === 'setup_database_unavailable' ? 'database' : undefined);
      if (target) setStep(target);
    } finally {
      setBusy(false);
    }
  };

  // Field-level codes render next to their inputs and section codes in sectionAlert; anything
  // else (network, conflicts, invalid settings) is summarized here with any details.
  const handledInline =
    error instanceof ApiError && (error.code === 'validation_failed' || Object.keys(error.fieldErrors).some((f) => !f.includes('.')));
  const globalError =
    error !== null && !handledInline ? (
      <div className="alert alert-danger py-2" role="alert">
        <p className="mb-1">{errorMessage(error, t)}</p>
        {error instanceof ApiError && error.details.length > 0 && (
          <ul className="mb-0 small">
            {error.details.map((detail) => (
              <li key={detail}>{detail}</li>
            ))}
          </ul>
        )}
      </div>
    ) : null;

  const nav = (
    <div className="cx-setup-actions">
      {step !== 'database' && (
        <button type="button" className="btn btn-outline-secondary" onClick={back} disabled={busy}>
          {t('setup.back')}
        </button>
      )}
      <button type="submit" className="btn btn-primary ms-auto" disabled={busy}>
        {t('setup.next')}
      </button>
    </div>
  );

  let body: ReactNode;
  if (phase !== 'wizard') {
    body = (
      <section aria-labelledby="setup-step-title">
        <h2 id="setup-step-title" ref={heading} tabIndex={-1} className="cx-card-title">
          {t(`setup.phase.${phase}.title`)}
        </h2>
        {phase === 'installing' || phase === 'restarting' ? (
          <p role="status" className="d-flex align-items-center gap-2">
            <span className="spinner-border spinner-border-sm" aria-hidden="true" />
            {t(`setup.phase.${phase}.body`)}
          </p>
        ) : (
          <>
            <FormAlert tone={phase === 'done' ? 'success' : 'info'}>{t(`setup.phase.${phase}.body`, { email: adminEmail })}</FormAlert>
            <button type="button" className="btn btn-primary" onClick={() => window.location.assign('/sign-in')}>
              {t('setup.phase.done.signIn')}
            </button>
          </>
        )}
      </section>
    );
  } else if (step === 'code' || !form || !config) {
    body = (
      <form onSubmit={submitCode} noValidate aria-labelledby="setup-step-title">
        <h2 id="setup-step-title" ref={heading} tabIndex={-1} className="cx-card-title">
          {t('setup.code.title')}
        </h2>
        <p className="cx-muted">{t('setup.code.intro')}</p>
        {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
        <Field
          label={t('setup.code.label')}
          autoComplete="off"
          spellCheck={false}
          required
          value={token}
          onChange={(e) => setToken(e.target.value)}
          hint={t('setup.code.hint')}
        />
        <button type="submit" className="btn btn-primary" disabled={busy || !token.trim()}>
          {busy ? t('auth.working') : t('setup.code.submit')}
        </button>
      </form>
    );
  } else if (step === 'database') {
    body = (
      <form onSubmit={next} noValidate aria-labelledby="setup-step-title">
        <h2 id="setup-step-title" ref={heading} tabIndex={-1} className="cx-card-title">
          {t('setup.database.title')}
        </h2>
        {globalError}
        {sectionAlert('database')}
        {locked ? (
          <FormAlert tone="info">{t('setup.database.locked', { provider: t(`setup.database.providers.${config.database.provider ?? 'Sqlite'}`) })}</FormAlert>
        ) : (
          <>
            <ChoiceGroup
              legend={t('setup.database.provider')}
              name="db-provider"
              value={form.dbProvider}
              options={config.database.supportedProviders.map((p) => ({ value: p, label: t(`setup.database.providers.${p}`), hint: t(`setup.database.hints.${p}`) }))}
              onChange={(value) => set('dbProvider', value as DatabaseProvider)}
            />
            {form.dbProvider === 'Sqlite' ? (
              <Field label={t('setup.database.path')} value={form.sqlitePath} onChange={(e) => set('sqlitePath', e.target.value)} hint={t('setup.database.pathHint')} errors={messages('database.path')} />
            ) : (
              <>
                <div className="row g-2">
                  <Field className="col-sm-8" label={t('setup.database.host')} value={form.pgHost} onChange={(e) => set('pgHost', e.target.value)} errors={messages('database.host')} />
                  <Field className="col-sm-4" label={t('setup.database.port')} inputMode="numeric" value={form.pgPort} onChange={(e) => set('pgPort', e.target.value)} errors={messages('database.port')} />
                </div>
                <Field label={t('setup.database.name')} value={form.pgDatabase} onChange={(e) => set('pgDatabase', e.target.value)} hint={t('setup.database.nameHint')} errors={messages('database.database')} />
                <div className="row g-2">
                  <Field className="col-sm-6" label={t('setup.database.username')} autoComplete="off" value={form.pgUsername} onChange={(e) => set('pgUsername', e.target.value)} errors={messages('database.username')} />
                  <Field className="col-sm-6" label={t('setup.database.password')} type="password" autoComplete="new-password" value={form.pgPassword} onChange={(e) => set('pgPassword', e.target.value)} />
                </div>
                <Select label={t('setup.database.sslMode')} value={form.pgSslMode} options={['Prefer', 'Require', 'Disable'].map((m) => ({ value: m, label: t(`setup.database.ssl.${m}`) }))} onChange={(v) => set('pgSslMode', v)} />
              </>
            )}
          </>
        )}
        {notice && <FormAlert tone={notice.tone}>{notice.text}</FormAlert>}
        <button type="button" className="btn btn-outline-primary mb-3" onClick={testDatabase} disabled={busy}>
          {busy ? t('auth.working') : t('setup.database.test')}
        </button>
        {nav}
      </form>
    );
  } else if (step === 'email') {
    body = (
      <form onSubmit={next} noValidate aria-labelledby="setup-step-title">
        <h2 id="setup-step-title" ref={heading} tabIndex={-1} className="cx-card-title">
          {t('setup.email.title')}
        </h2>
        <p className="cx-muted">{t('setup.email.intro')}</p>
        {globalError}
        {sectionAlert('email')}
        <ChoiceGroup
          legend={t('setup.email.provider')}
          name="email-provider"
          value={form.emailProvider}
          options={(['Smtp', 'Postmark'] as const).map((p) => ({ value: p, label: t(`setup.email.providers.${p}`) }))}
          onChange={(value) => set('emailProvider', value as EmailProvider)}
        />
        <div className="row g-2">
          <Field className="col-sm-7" label={t('setup.email.fromAddress')} type="email" value={form.fromAddress} onChange={(e) => set('fromAddress', e.target.value)} errors={messages('email.fromAddress')} />
          <Field className="col-sm-5" label={t('setup.email.fromName')} value={form.fromName} onChange={(e) => set('fromName', e.target.value)} />
        </div>
        {form.emailProvider === 'Smtp' ? (
          <>
            <div className="row g-2">
              <Field className="col-sm-8" label={t('setup.email.host')} value={form.smtpHost} onChange={(e) => set('smtpHost', e.target.value)} errors={messages('email.smtpHost')} />
              <Field className="col-sm-4" label={t('setup.email.port')} inputMode="numeric" value={form.smtpPort} onChange={(e) => set('smtpPort', e.target.value)} errors={messages('email.smtpPort')} />
            </div>
            <Select
              label={t('setup.email.security')}
              value={form.smtpSecurity}
              options={config.email.smtpSecurityModes.map((m) => ({ value: m, label: t(`setup.email.securityModes.${m}`, { defaultValue: m }) }))}
              onChange={(v) => set('smtpSecurity', v)}
            />
            <div className="row g-2">
              <Field className="col-sm-6" label={t('setup.email.username')} autoComplete="off" value={form.smtpUsername} onChange={(e) => set('smtpUsername', e.target.value)} errors={messages('email.smtpCredentials')} />
              <Field className="col-sm-6" label={t('setup.email.password')} type="password" autoComplete="new-password" value={form.smtpPassword} onChange={(e) => set('smtpPassword', e.target.value)} />
            </div>
            <p className="form-text">{t('setup.email.credentialsHint')}</p>
          </>
        ) : (
          <Field label={t('setup.email.postmarkToken')} type="password" autoComplete="off" value={form.postmarkToken} onChange={(e) => set('postmarkToken', e.target.value)} errors={messages('email.postmarkToken')} />
        )}
        <div className="cx-subpanel mb-3">
          <div className="cx-inline-form">
            <Field className="flex-grow-1 mb-0" label={t('setup.email.testRecipient')} type="email" value={form.testRecipient} onChange={(e) => set('testRecipient', e.target.value)} errors={messages('email.testRecipient')} />
            <button type="button" className="btn btn-outline-primary" onClick={testEmail} disabled={busy}>
              {busy ? t('auth.working') : t('setup.email.test')}
            </button>
          </div>
          {notice && <div className="mt-2"><FormAlert tone={notice.tone}>{notice.text}</FormAlert></div>}
        </div>
        {nav}
      </form>
    );
  } else if (step === 'administrator') {
    body = (
      <form onSubmit={next} noValidate aria-labelledby="setup-step-title">
        <h2 id="setup-step-title" ref={heading} tabIndex={-1} className="cx-card-title">
          {t('setup.administrator.title')}
        </h2>
        <p className="cx-muted">{t('setup.administrator.intro')}</p>
        {globalError}
        <Field label={t('auth.fields.displayName')} autoComplete="name" maxLength={80} value={form.adminName} onChange={(e) => set('adminName', e.target.value)} errors={messages('administrator.displayName')} />
        <Field label={t('auth.fields.email')} type="email" autoComplete="email" value={form.adminEmail} onChange={(e) => set('adminEmail', e.target.value)} errors={messages('administrator.email')} />
        <Field
          label={t('auth.fields.password')}
          type="password"
          autoComplete="new-password"
          value={form.adminPassword}
          onChange={(e) => set('adminPassword', e.target.value)}
          hint={t('auth.passwordHint', { count: config.passwordMinimumLength })}
          errors={messages('administrator.password')}
        />
        <Field label={t('setup.administrator.confirm')} type="password" autoComplete="new-password" value={form.adminConfirm} onChange={(e) => set('adminConfirm', e.target.value)} errors={messages('administrator.confirm')} />
        {nav}
      </form>
    );
  } else if (step === 'options') {
    body = (
      <form onSubmit={next} noValidate aria-labelledby="setup-step-title">
        <h2 id="setup-step-title" ref={heading} tabIndex={-1} className="cx-card-title">
          {t('setup.options.title')}
        </h2>
        {globalError}
        {sectionAlert('options')}
        {sectionAlert('options.captcha')}
        <div className="cx-subpanel mb-3">
          <div className="form-check form-switch">
            <input id="setup-dev-mode" className="form-check-input" type="checkbox" role="switch" checked={form.developmentMode} onChange={(e) => set('developmentMode', e.target.checked)} />
            <label htmlFor="setup-dev-mode" className="form-check-label fw-semibold">
              {t('setup.options.developmentMode')}
            </label>
          </div>
          <p className="form-text mb-0">{t('setup.options.developmentModeHint')}</p>
        </div>
        <Field label={t('setup.options.publicBaseUrl')} type="url" value={form.publicBaseUrl} onChange={(e) => set('publicBaseUrl', e.target.value)} hint={t('setup.options.publicBaseUrlHint')} errors={messages('options.publicBaseUrl')} />
        <Select
          label={t('setup.options.defaultLanguage')}
          value={form.defaultLanguage}
          options={config.options.supportedLanguages.map((l) => ({ value: l, label: t(`language.${l}`, { defaultValue: l }) }))}
          onChange={(v) => set('defaultLanguage', v)}
        />
        <ChoiceGroup
          legend={t('setup.options.captcha')}
          name="captcha-provider"
          value={form.captchaProvider}
          options={(['Turnstile', 'None'] as const).map((p) => ({ value: p, label: t(`setup.options.captchaProviders.${p}`) }))}
          onChange={(value) => set('captchaProvider', value as CaptchaProvider)}
        />
        {form.captchaProvider === 'Turnstile' && (
          <>
            <div className="row g-2">
              <Field className="col-sm-6" label={t('setup.options.siteKey')} autoComplete="off" value={form.captchaSiteKey} onChange={(e) => set('captchaSiteKey', e.target.value)} errors={messages('options.captchaKeys')} />
              <Field className="col-sm-6" label={t('setup.options.secretKey')} type="password" autoComplete="off" value={form.captchaSecretKey} onChange={(e) => set('captchaSecretKey', e.target.value)} />
            </div>
            <p className="form-text">{t(config.options.captchaKeysConfigured ? 'setup.options.keysConfigured' : 'setup.options.keysRequired')}</p>
          </>
        )}
        {nav}
      </form>
    );
  } else {
    const rows: [string, string][] = [
      [
        t('setup.review.database'),
        locked
          ? t('setup.review.lockedDatabase')
          : form.dbProvider === 'Sqlite'
            ? `${t('setup.database.providers.Sqlite')} · ${form.sqlitePath}`
            : `${t('setup.database.providers.PostgreSql')} · ${form.pgUsername}@${form.pgHost}:${form.pgPort}/${form.pgDatabase}`,
      ],
      [
        t('setup.review.email'),
        form.emailProvider === 'Smtp'
          ? `SMTP · ${form.smtpHost}:${form.smtpPort} · ${form.fromAddress}`
          : `Postmark · ${form.fromAddress}`,
      ],
      [t('setup.review.administrator'), `${form.adminName} <${form.adminEmail}>`],
      [t('setup.review.mode'), t(form.developmentMode ? 'setup.review.development' : 'setup.review.production')],
      [t('setup.options.publicBaseUrl'), form.publicBaseUrl],
      [t('setup.options.defaultLanguage'), t(`language.${form.defaultLanguage}`, { defaultValue: form.defaultLanguage })],
      [t('setup.options.captcha'), t(`setup.options.captchaProviders.${form.captchaProvider}`)],
    ];
    body = (
      <section aria-labelledby="setup-step-title">
        <h2 id="setup-step-title" ref={heading} tabIndex={-1} className="cx-card-title">
          {t('setup.review.title')}
        </h2>
        <p className="cx-muted">{t('setup.review.intro')}</p>
        {globalError}
        <dl className="cx-setup-summary">
          {rows.map(([label, value]) => (
            <div key={label}>
              <dt>{label}</dt>
              <dd>{value}</dd>
            </div>
          ))}
        </dl>
        <div className="cx-setup-actions">
          <button type="button" className="btn btn-outline-secondary" onClick={back} disabled={busy}>
            {t('setup.back')}
          </button>
          <button type="button" className="btn btn-primary ms-auto" onClick={() => void install()} disabled={busy}>
            {t('setup.review.install')}
          </button>
        </div>
      </section>
    );
  }

  const current = steps.indexOf(step);
  return (
    <div className="cx-setup">
      <header className="cx-setup-header">
        <div className="cx-brand">
          <BrandMark />
          <span>
            <span className="cx-product d-block">{t('app.name')}</span>
            <span className="cx-setup-subtitle">{t('setup.title')}</span>
          </span>
        </div>
        <LanguageSelector />
      </header>
      <main className="cx-setup-main">
        <h1 className="visually-hidden">{t('setup.title')}</h1>
        {phase === 'wizard' && step !== 'code' && (
          <ol className="cx-steps" aria-label={t('setup.progress')}>
            {steps.slice(1).map((s, i) => (
              <li key={s} className={i + 1 < current ? 'is-done' : i + 1 === current ? 'is-current' : undefined} aria-current={s === step ? 'step' : undefined}>
                <span className="cx-step-number" aria-hidden="true">
                  {i + 1}
                </span>
                <span>{t(`setup.steps.${s}`)}</span>
              </li>
            ))}
          </ol>
        )}
        <div className="cx-card cx-setup-card">{body}</div>
      </main>
    </div>
  );
}

function ChoiceGroup({
  legend,
  name,
  value,
  options,
  onChange,
}: {
  legend: string;
  name: string;
  value: string;
  options: { value: string; label: string; hint?: string }[];
  onChange: (value: string) => void;
}) {
  return (
    <fieldset className="mb-3">
      <legend className="form-label fs-6">{legend}</legend>
      <div className="cx-choices">
        {options.map((option) => (
          <label key={option.value} className={`cx-choice${option.value === value ? ' is-selected' : ''}`}>
            <input type="radio" className="form-check-input" name={name} value={option.value} checked={option.value === value} onChange={() => onChange(option.value)} />
            <span>
              <span className="d-block fw-semibold">{option.label}</span>
              {option.hint && <span className="d-block small cx-muted">{option.hint}</span>}
            </span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}

function Select({ label, value, options, onChange }: { label: string; value: string; options: { value: string; label: string }[]; onChange: (value: string) => void }) {
  const id = `setup-select-${label.replace(/\W+/g, '-').toLowerCase()}`;
  return (
    <div className="mb-3">
      <label htmlFor={id} className="form-label">
        {label}
      </label>
      <select id={id} className="form-select" value={value} onChange={(e) => onChange(e.target.value)}>
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </div>
  );
}
