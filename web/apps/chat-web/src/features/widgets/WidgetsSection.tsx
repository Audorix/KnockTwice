import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import type { UserSummary, WidgetLanguage, WidgetPosition, WidgetResponse } from '../../shared/api/contracts';
import { Field, FormAlert } from '../../shared/components/Field';
import { errorMessage } from '../auth/errors';
import * as api from './widgetsApi';

/**
 * Website widgets of an organization (Phase 10): create, configure, choose who answers, switch on
 * and copy the embed snippet. The signing secret is shown once, when created or replaced.
 */
export function WidgetsSection({ organizationId, members }: { organizationId: string; members: UserSummary[] }) {
  const { t } = useTranslation();
  const [widgets, setWidgets] = useState<WidgetResponse[] | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [secret, setSecret] = useState<{ widgetId: string; value: string } | null>(null);
  const [name, setName] = useState('');
  const [error, setError] = useState<unknown>(null);

  const reload = useCallback(() => {
    api.listWidgets(organizationId).then(setWidgets, (err: unknown) => {
      setWidgets([]);
      setError(err);
    });
  }, [organizationId]);
  useEffect(reload, [reload]);

  const create = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    try {
      const created = await api.createWidget(organizationId, name.trim());
      setName('');
      setSecret({ widgetId: created.widget.id, value: created.signingSecret });
      setSelected(created.widget.id);
      setWidgets((current) => [...(current ?? []), created.widget]);
    } catch (err) {
      setError(err);
    }
  };

  const replace = (widget: WidgetResponse) => setWidgets((current) => current?.map((w) => (w.id === widget.id ? widget : w)) ?? null);
  const open = widgets?.find((w) => w.id === selected) ?? null;

  return (
    <section className="cx-card mt-3" aria-labelledby="widgets-title">
      <h2 id="widgets-title" className="cx-card-title">
        {t('widgets.title')}
      </h2>
      <p className="cx-muted">{t('widgets.intro')}</p>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}

      {widgets === null ? (
        <p className="cx-muted">{t('auth.working')}</p>
      ) : widgets.length === 0 ? (
        <p className="cx-muted">{t('widgets.none')}</p>
      ) : (
        <ul className="list-unstyled cx-session-list">
          {widgets.map((widget) => (
            <li key={widget.id} className="cx-session">
              <span className="text-truncate">
                <strong>{widget.name}</strong>{' '}
                <span className={`badge ${widget.enabled ? 'text-bg-success' : 'text-bg-light border'}`}>{t(widget.enabled ? 'widgets.on' : 'widgets.off')}</span>
                <span className="cx-meta"> · {widget.allowedOrigins.join(', ') || t('widgets.noOrigins')}</span>
              </span>
              <button
                type="button"
                className="btn btn-sm btn-outline-secondary"
                aria-expanded={selected === widget.id}
                aria-controls="widget-editor"
                onClick={() => setSelected(selected === widget.id ? null : widget.id)}
              >
                {t('widgets.configure')}
              </button>
            </li>
          ))}
        </ul>
      )}

      {secret && (
        <div className="alert alert-warning" role="status">
          <p className="mb-1">
            <strong>{t('widgets.secretTitle')}</strong> {t('widgets.secretHint')}
          </p>
          <CopyField label={t('widgets.secret')} value={secret.value} />
          <button type="button" className="btn btn-sm btn-outline-secondary mt-2" onClick={() => setSecret(null)}>
            {t('widgets.secretDone')}
          </button>
        </div>
      )}

      {open && (
        <WidgetEditor
          key={open.id}
          organizationId={organizationId}
          widget={open}
          members={members}
          onSaved={replace}
          onSecret={(value) => setSecret({ widgetId: open.id, value })}
        />
      )}

      <form className="cx-inline-form mt-3" noValidate onSubmit={(e) => void create(e)}>
        <Field label={t('widgets.newName')} maxLength={80} value={name} onChange={(e) => setName(e.target.value)} className="flex-grow-1" />
        <button type="submit" className="btn btn-primary" disabled={!name.trim()}>
          {t('widgets.create')}
        </button>
      </form>
    </section>
  );
}

function WidgetEditor({
  organizationId,
  widget,
  members,
  onSaved,
  onSecret,
}: {
  organizationId: string;
  widget: WidgetResponse;
  members: UserSummary[];
  onSaved: (widget: WidgetResponse) => void;
  onSecret: (secret: string) => void;
}) {
  const { t } = useTranslation();
  const [form, setForm] = useState(() => ({
    name: widget.name,
    title: widget.title,
    welcomeMessage: widget.welcomeMessage ?? '',
    accentColor: widget.accentColor,
    position: widget.position,
    defaultLanguage: widget.defaultLanguage,
    allowAnonymous: widget.allowAnonymous,
    origins: widget.allowedOrigins.join('\n'),
    responderIds: widget.responders.map((r) => r.id),
    enabled: widget.enabled,
  }));
  const [error, setError] = useState<unknown>(null);
  const [saved, setSaved] = useState(false);
  const set = <K extends keyof typeof form>(key: K, value: (typeof form)[K]) => {
    setSaved(false);
    setForm((current) => ({ ...current, [key]: value }));
  };

  const save = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    try {
      const result = await api.updateWidget(organizationId, widget.id, {
        name: form.name,
        title: form.title,
        welcomeMessage: form.welcomeMessage,
        accentColor: form.accentColor,
        position: form.position,
        defaultLanguage: form.defaultLanguage,
        allowAnonymous: form.allowAnonymous,
        allowedOrigins: form.origins.split(/\s+/).map((o) => o.trim()).filter(Boolean),
        responderIds: form.responderIds,
        enabled: form.enabled,
      });
      onSaved(result);
      setForm((current) => ({ ...current, origins: result.allowedOrigins.join('\n'), enabled: result.enabled }));
      setSaved(true);
    } catch (err) {
      setError(err);
    }
  };

  const rotate = async () => {
    if (!window.confirm(t('widgets.rotateConfirm'))) return;
    setError(null);
    try {
      const result = await api.rotateSecret(organizationId, widget.id);
      onSaved(result.widget);
      onSecret(result.signingSecret);
    } catch (err) {
      setError(err);
    }
  };

  const toggleResponder = (id: string, on: boolean) =>
    set('responderIds', on ? [...form.responderIds, id] : form.responderIds.filter((x) => x !== id));

  return (
    <form id="widget-editor" className="cx-subpanel" onSubmit={(e) => void save(e)} noValidate aria-label={t('widgets.editorFor', { name: widget.name })}>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {saved && <FormAlert tone="success">{t('widgets.saved')}</FormAlert>}

      <div className="row g-2">
        <div className="col-sm-6">
          <Field label={t('widgets.name')} maxLength={80} value={form.name} onChange={(e) => set('name', e.target.value)} />
        </div>
        <div className="col-sm-6">
          <Field label={t('widgets.chatTitle')} maxLength={80} value={form.title} onChange={(e) => set('title', e.target.value)} />
        </div>
      </div>

      <div className="mb-3">
        <label htmlFor="widget-welcome" className="form-label">{t('widgets.welcome')}</label>
        <textarea id="widget-welcome" className="form-control" rows={2} maxLength={500} value={form.welcomeMessage} onChange={(e) => set('welcomeMessage', e.target.value)} />
      </div>

      <div className="row g-2">
        <div className="col-sm-4 mb-3">
          <label htmlFor="widget-color" className="form-label">{t('widgets.color')}</label>
          <input id="widget-color" type="color" className="form-control form-control-color" value={form.accentColor} onChange={(e) => set('accentColor', e.target.value)} />
        </div>
        <div className="col-sm-4 mb-3">
          <label htmlFor="widget-position" className="form-label">{t('widgets.position')}</label>
          <select id="widget-position" className="form-select" value={form.position} onChange={(e) => set('position', e.target.value as WidgetPosition)}>
            <option value="right">{t('widgets.positions.right')}</option>
            <option value="left">{t('widgets.positions.left')}</option>
          </select>
        </div>
        <div className="col-sm-4 mb-3">
          <label htmlFor="widget-language" className="form-label">{t('widgets.language')}</label>
          <select id="widget-language" className="form-select" value={form.defaultLanguage} onChange={(e) => set('defaultLanguage', e.target.value as WidgetLanguage)}>
            <option value="auto">{t('widgets.languages.auto')}</option>
            <option value="en">English</option>
            <option value="es">Español</option>
          </select>
        </div>
      </div>

      <div className="mb-3">
        <label htmlFor="widget-origins" className="form-label">{t('widgets.origins')}</label>
        <textarea id="widget-origins" className="form-control font-monospace" rows={3} value={form.origins} placeholder="https://www.example.org"
          aria-describedby="widget-origins-hint" onChange={(e) => set('origins', e.target.value)} />
        <div id="widget-origins-hint" className="form-text">{t('widgets.originsHint')}</div>
      </div>

      <fieldset className="mb-3">
        <legend className="form-label fs-6">{t('widgets.responders')}</legend>
        <div className="form-text mb-1">{t('widgets.respondersHint')}</div>
        {members.map((m) => (
          <div key={m.id} className="form-check">
            <input id={`responder-${m.id}`} className="form-check-input" type="checkbox" checked={form.responderIds.includes(m.id)}
              onChange={(e) => toggleResponder(m.id, e.target.checked)} />
            <label className="form-check-label" htmlFor={`responder-${m.id}`}>{m.displayName}</label>
          </div>
        ))}
      </fieldset>

      <div className="form-check form-switch mb-2">
        <input id="widget-anonymous" className="form-check-input" type="checkbox" role="switch" checked={form.allowAnonymous} onChange={(e) => set('allowAnonymous', e.target.checked)} />
        <label className="form-check-label" htmlFor="widget-anonymous">{t('widgets.anonymous')}</label>
      </div>
      <div className="form-check form-switch mb-3">
        <input id="widget-enabled" className="form-check-input" type="checkbox" role="switch" checked={form.enabled} onChange={(e) => set('enabled', e.target.checked)} />
        <label className="form-check-label" htmlFor="widget-enabled">{t('widgets.enabled')}</label>
      </div>

      <div className="d-flex flex-wrap gap-2">
        <button type="submit" className="btn btn-primary">{t('account.save')}</button>
        <button type="button" className="btn btn-outline-danger" onClick={() => void rotate()}>{t('widgets.rotate')}</button>
      </div>

      <h3 className="h6 mt-4">{t('widgets.embedTitle')}</h3>
      <p className="cx-muted mb-1">{t('widgets.embedHint')}</p>
      <CopyField label={t('widgets.snippet')} value={api.embedSnippet(window.location.origin, widget.id)} />
      <p className="cx-muted small mt-2 mb-0">{t('widgets.identityHint', { id: widget.id })}</p>
    </form>
  );
}

function CopyField({ label, value }: { label: string; value: string }) {
  const { t } = useTranslation();
  const [copied, setCopied] = useState(false);
  return (
    <div className="input-group input-group-sm">
      <input className="form-control font-monospace" readOnly value={value} aria-label={label} onFocus={(e) => e.target.select()} />
      <button
        type="button"
        className="btn btn-outline-secondary"
        onClick={() =>
          void navigator.clipboard?.writeText(value).then(() => {
            setCopied(true);
            setTimeout(() => setCopied(false), 2000);
          })
        }
      >
        {copied ? t('widgets.copied') : t('widgets.copy')}
      </button>
    </div>
  );
}
