import { useEffect, useId, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { getJson, sendJson } from '../../shared/api/httpClient';
import { trapTab, useConfirm } from '../../shared/components/ConfirmDialog';
import { FormAlert } from '../../shared/components/Field';
import { NavIcon, type NavIconName } from '../../shared/components/NavIcon';
import { useAuth } from '../auth/AuthContext';
import { errorMessage } from '../auth/errors';

interface DemoCounts {
  users: number;
  organizations: number;
  contacts: number;
  conversations: number;
  messages: number;
  /** Mentions, saved messages, reminders and scheduled messages. */
  activity: number;
  /** Support conversations from demo customers (also counted in conversations). */
  support?: number;
  /** Demo people with a profile photo from the demo avatars folder. */
  photos?: number;
}

interface DemoDataResponse {
  created: DemoCounts;
  total: DemoCounts;
  userEmails: string[];
  password: string;
}

interface DemoDataStep {
  key: string;
  done: number;
  total: number;
}

/** A load or clearing running in the background on the server (2026-11-25), with progress per part. */
interface DemoDataJob {
  id: string;
  /** Missing from servers before clearing ran as a job: a load. */
  kind?: 'load' | 'clear';
  state: 'running' | 'completed' | 'failed';
  steps: DemoDataStep[];
  /** What a completed load added. */
  result: DemoDataResponse | null;
  /** What a completed clearing removed. */
  removed?: DemoCounts | null;
  errorCode: string | null;
}

const none: DemoCounts = { users: 0, organizations: 0, contacts: 0, conversations: 0, messages: 0, activity: 0, support: 0, photos: 0 };
const sum = (c: DemoCounts) => c.users + c.organizations + c.contacts + c.conversations + c.messages + (c.activity ?? 0) + (c.photos ?? 0);
const percent = (done: number, total: number) => (total <= 0 ? 100 : Math.min(100, Math.floor((done * 100) / total)));
const kindOf = (job: DemoDataJob) => job.kind ?? 'load';
const pollMs = 1000;

/**
 * Demo data for installation administrators where it is enabled (Development and development-mode
 * installations): load it (safe to repeat; only missing data is added) or clear it (only demo data;
 * what real people created stays). Both run in the background on the server and show progress per
 * part; when everything is at 100%, a dialog confirms it and OK hides the progress.
 */
export function DemoDataCard() {
  const { t, i18n } = useTranslation();
  const { user, config } = useAuth();
  const [busy, setBusy] = useState(false);
  const [existing, setExisting] = useState<DemoCounts | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [accounts, setAccounts] = useState<DemoDataResponse | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [failed, setFailed] = useState<'load' | 'clear' | null>(null);
  /** The job whose progress is shown; null hides the progress. */
  const [job, setJob] = useState<DemoDataJob | null>(null);
  /** The finished job the confirmation dialog shows. */
  const [done, setDone] = useState<DemoDataJob | null>(null);
  const [confirm, confirmDialog] = useConfirm();
  const allowed = !!user && !!config?.demoDataEnabled && user.permissions.includes('installation.demo_data');
  const running = job?.state === 'running';

  /** Applies a job's latest state: on completion, the dialog and the new counts. */
  const follow = (next: DemoDataJob) => {
    setJob(next);
    if (next.state === 'completed') {
      setDone(next);
      if (kindOf(next) === 'clear') {
        setAccounts(null);
        setExisting(none);
      } else if (next.result) {
        setAccounts(next.result);
        setExisting(next.result.total);
      }
    } else if (next.state === 'failed') {
      setFailed(kindOf(next));
    }
  };

  useEffect(() => {
    if (!allowed) return;
    getJson<DemoCounts>('/api/v1/admin/demo-data').then((r) => setExisting(r.body ?? null), () => undefined);
    // A job started earlier (in another tab, or before a reload) that is still running: follow it.
    getJson<DemoDataJob>('/api/v1/admin/demo-data/jobs/current').then((r) => {
      if (r.body?.state === 'running') setJob(r.body);
    }, () => undefined);
  }, [allowed]);

  // Follows a running job until it completes or fails.
  useEffect(() => {
    if (!running) return;
    let cancelled = false;
    const timer = window.setTimeout(() => {
      getJson<DemoDataJob>('/api/v1/admin/demo-data/jobs/current').then((r) => {
        if (!cancelled && r.body) follow(r.body);
      }, () => {
        // A missed poll (network hiccup, API restart) is retried on the next tick.
        if (!cancelled) setJob((current) => (current ? { ...current } : current));
      });
    }, pollMs);
    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
    // follow only sets state; the poll restarts whenever the job changes.
  }, [running, job]);

  if (!allowed) return null;

  const start = async (action: 'load' | 'clear') => {
    setBusy(true);
    setError(null);
    setNotice(null);
    setFailed(null);
    try {
      follow((await sendJson<DemoDataJob>('POST', `/api/v1/admin/demo-data/jobs${action === 'clear' ? '?action=clear' : ''}`))!);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const closeDone = () => {
    const finished = done;
    setDone(null);
    setJob(null);
    if (!finished) return;
    if (kindOf(finished) === 'clear') {
      const removed = finished.removed ?? none;
      setNotice(t('demo.cleared', { users: removed.users, organizations: removed.organizations, conversations: removed.conversations, messages: removed.messages }));
    } else if (finished.result) {
      const created = finished.result.created;
      setNotice(sum(created) === 0
        ? t('demo.upToDate')
        : t('demo.created', {
          users: created.users, organizations: created.organizations, contacts: created.contacts,
          conversations: created.conversations, messages: created.messages, activity: created.activity,
          support: created.support ?? 0, photos: created.photos ?? 0,
        }));
    }
  };

  const clear = async () => {
    const ok = await confirm({
      title: t('confirm.clearDemo.title'),
      text: t('confirm.clearDemo.text'),
      confirmLabel: t('confirm.clearDemo.action'),
    });
    if (ok) await start('clear');
  };

  const tiles: { key: string; icon: NavIconName; value: number }[] = existing
    ? [
        { key: 'people', icon: 'people', value: existing.users },
        { key: 'companies', icon: 'building', value: existing.organizations },
        { key: 'conversations', icon: 'chats', value: existing.conversations },
        { key: 'messages', icon: 'mail', value: existing.messages },
        { key: 'activity', icon: 'at', value: existing.activity ?? 0 },
        { key: 'support', icon: 'headset', value: existing.support ?? 0 },
      ]
    : [];
  const number = (value: number) => value.toLocaleString(i18n.language);
  const clearing = !!job && kindOf(job) === 'clear';
  // Every part counts the same, so the total reaches 100% only when every part has (messages alone
  // would otherwise outweigh everything else).
  const overall = job && job.steps.length > 0
    ? Math.floor(job.steps.reduce((a, s) => a + percent(s.done, s.total), 0) / job.steps.length)
    : 0;
  return (
    <section className="cx-card cx-demo-card" aria-labelledby="demo-title">
      {confirmDialog}
      {done && <DemoDataDoneDialog job={done} onClose={closeDone} />}
      <h2 id="demo-title" className="cx-card-title">
        {t('demo.title')}
      </h2>
      <p className="mb-1">{t('demo.lead')}</p>
      <p className="cx-muted small">{t('demo.intro')}</p>
      {existing && (
        <>
          <h3 className="cx-demo-loaded">{t('demo.loaded')}</h3>
          <ul className="list-unstyled cx-demo-tiles">
            {tiles.map((tile) => (
              <li key={tile.key} className="cx-demo-tile">
                <NavIcon name={tile.icon} size={22} />
                <span>
                  <strong>{number(tile.value)}</strong>
                  <span className="cx-muted small d-block">{t(`demo.tiles.${tile.key}`)}</span>
                </span>
              </li>
            ))}
          </ul>
        </>
      )}
      {job && (
        <div className={`cx-demo-progress${clearing ? ' is-clearing' : ''}`} aria-labelledby="demo-progress-title">
          <div className="cx-demo-progress-head">
            <h3 id="demo-progress-title" className="cx-demo-loaded mb-0">{t(clearing ? 'demo.clearingTitle' : 'demo.progressTitle')}</h3>
            <span className="cx-demo-progress-overall" aria-live="polite">{t('demo.progressOverall', { percent: overall })}</span>
          </div>
          <ul className="list-unstyled mb-0">
            {job.steps.map((step, index) => {
              const value = percent(step.done, step.total);
              const label = t(clearing ? `demo.clearSteps.${step.key}` : `demo.steps.${step.key}`, { defaultValue: step.key });
              // The part being worked on moves even when it is done in one step (clearing removes most
              // parts with one statement each, so their count jumps from 0 to the total).
              const active = running && value < 100 && job.steps.findIndex((s) => percent(s.done, s.total) < 100) === index;
              return (
                <li key={step.key} className={`cx-demo-step${value === 100 ? ' is-done' : ''}${active ? ' is-active' : ''}`}>
                  <div className="cx-demo-step-head">
                    <span className="cx-demo-step-name">
                      {value === 100 && <NavIcon name="check" size={14} />}
                      {label}
                    </span>
                    <span className="cx-muted small">
                      {t('demo.progressCount', { done: number(step.done), total: number(step.total) })} · {value}%
                    </span>
                  </div>
                  <div className="progress cx-demo-bar" role="progressbar" aria-label={label} aria-valuemin={0} aria-valuemax={100} aria-valuenow={value}>
                    <div className={`progress-bar${active ? ' progress-bar-striped progress-bar-animated' : ''}${active && value === 0 ? ' is-pending' : ''}`}
                      style={{ width: `${active && value === 0 ? 100 : value}%` }} />
                  </div>
                </li>
              );
            })}
          </ul>
        </div>
      )}
      {failed && <FormAlert tone="danger">{t(failed === 'clear' ? 'demo.clearFailed' : 'demo.failed')}</FormAlert>}
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {notice && (
        <div role="status">
          <FormAlert tone="success">{notice}</FormAlert>
        </div>
      )}
      {accounts && !job && (
        <>
          <p className="small mb-1">{t('demo.signInAs', { password: accounts.password })}</p>
          <p className="small cx-meta">{accounts.userEmails.join(', ')}</p>
        </>
      )}
      <div className="d-flex flex-wrap gap-2">
        <button type="button" className="btn btn-primary" onClick={() => void start('load')} disabled={busy || running}>
          {running && !clearing ? t('demo.loading') : busy ? t('auth.working') : t('demo.populate')}
        </button>
        <button type="button" className="btn btn-outline-danger" onClick={() => void clear()} disabled={busy || running || !existing || sum(existing) === 0}>
          {running && clearing ? t('demo.clearing') : t('demo.clear')}
        </button>
      </div>
      <p className="cx-muted small mt-2 mb-0">{t('demo.clearNote')}</p>
    </section>
  );
}

/**
 * Confirms a finished job. A load: what is there now and how to sign in as a demo person. Clearing:
 * what was removed. OK closes it.
 */
function DemoDataDoneDialog({ job, onClose }: { job: DemoDataJob; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const ok = useRef<HTMLButtonElement>(null);
  const id = useId();
  useEffect(() => {
    const opener = document.activeElement;
    ok.current?.focus();
    return () => (opener as HTMLElement | null)?.focus?.();
  }, []);
  const cleared = kindOf(job) === 'clear';
  const counts = (cleared ? job.removed : job.result?.total) ?? none;
  const items: { key: string; value: number }[] = [
    { key: 'people', value: counts.users },
    { key: 'companies', value: counts.organizations },
    { key: 'conversations', value: counts.conversations },
    { key: 'messages', value: counts.messages },
    { key: 'activity', value: counts.activity ?? 0 },
    { key: 'support', value: counts.support ?? 0 },
  ];
  const text = cleared
    ? (sum(counts) === 0 ? t('demo.clearedNothing') : t('demo.clearedText'))
    : (job.result && sum(job.result.created) === 0 ? t('demo.doneUpToDate') : t('demo.doneText'));
  return (
    <div className="cx-dialog-backdrop" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="cx-dialog cx-confirm-dialog cx-demo-done" role="dialog" aria-modal="true" aria-labelledby={`${id}-title`}
        aria-describedby={`${id}-text`} onKeyDown={(e) => {
          if (e.key === 'Escape') {
            e.stopPropagation();
            onClose();
          } else {
            trapTab(e);
          }
        }}>
        <button type="button" className="cx-tool cx-confirm-close" onClick={onClose} aria-label={t('confirm.close')}>
          <NavIcon name="close" size={20} />
        </button>
        <span className="cx-confirm-icon is-success" aria-hidden="true">
          <NavIcon name={cleared ? 'trash' : 'check'} size={28} />
        </span>
        <h2 id={`${id}-title`} className="cx-confirm-title">{t(cleared ? 'demo.clearedTitle' : 'demo.doneTitle')}</h2>
        <p id={`${id}-text`} className="cx-confirm-text">{text}</p>
        <ul className="list-unstyled cx-demo-done-list">
          {items.map((item) => (
            <li key={item.key}>
              <strong>{item.value.toLocaleString(i18n.language)}</strong>
              <span className="cx-muted small">{t(`demo.tiles.${item.key}`)}</span>
            </li>
          ))}
        </ul>
        {cleared ? (
          <p className="small mb-0">{t('demo.clearedKept')}</p>
        ) : job.result && (
          <>
            <p className="small mb-1">{t('demo.signInAs', { password: job.result.password })}</p>
            <p className="small cx-meta">{job.result.userEmails.slice(0, 3).join(', ')}</p>
          </>
        )}
        <div className="cx-confirm-actions">
          <button ref={ok} type="button" className="btn btn-primary" onClick={onClose}>{t('demo.doneOk')}</button>
        </div>
      </div>
    </div>
  );
}
