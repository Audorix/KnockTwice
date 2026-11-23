import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { navigate } from '../../app/router';
import { Avatar } from '../../shared/components/Avatar';
import { FormAlert } from '../../shared/components/Field';
import { formatDateTime } from '../../shared/format';
import { toPlainText } from '../../shared/richText';
import { errorMessage } from '../auth/errors';
import { messageHref } from '../conversations/conversationParts';
import { useRealtime } from '../realtime/RealtimeContext';
import * as planning from './planningApi';
import { localInputValue } from './TimeMenu';

/** Phase 20 (ADR-034): the Scheduled and Reminders tabs of the Activity page. */

const place = (type: string | null, title: string | null, t: (key: string) => string) =>
  type === 'direct' ? t('mentions.direct') : type === 'channel' ? `# ${title ?? ''}` : title ?? t('conversations.someone');

/** Reloads when the server says scheduled messages or reminders changed (also from other devices). */
function useLive<T>(load: () => Promise<T[]>, kind: 'scheduled' | 'reminders') {
  const { client } = useRealtime();
  const [items, setItems] = useState<T[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const reload = useCallback(() => {
    load().then(setItems, setError);
  }, [load]);
  useEffect(reload, [reload]);
  useEffect(() => {
    if (!client) return;
    const offs = [client.onPlanning((event) => event.kind === kind && reload()), client.onReminderDue(() => kind === 'reminders' && reload()), client.onReconnected(reload)];
    return () => offs.forEach((off) => off());
  }, [client, kind, reload]);
  return { items, setItems, error, setError, reload };
}

export function ScheduledList() {
  const { t, i18n } = useTranslation();
  const { items, error, setError, reload } = useLive(planning.listScheduled, 'scheduled');
  const [editing, setEditing] = useState<{ id: string; body: string; at: string } | null>(null);

  const act = async (action: () => Promise<unknown>) => {
    setError(null);
    try {
      await action();
      setEditing(null);
      reload();
    } catch (err) {
      setError(err);
    }
  };

  return (
    <>
      <p className="cx-muted">{t('planning.scheduledIntro')}</p>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {items === null ? (
        error === null && <p className="cx-muted">{t('auth.working')}</p>
      ) : items.length === 0 ? (
        <p className="cx-muted mb-0">{t('planning.scheduledEmpty')}</p>
      ) : (
        <ul className="list-unstyled cx-mention-list mb-0">
          {items.map((item) => (
            <li key={item.id} className="cx-planning-item">
              <div className="d-flex gap-2 align-items-baseline flex-wrap">
                <strong>{place(item.conversationType, item.conversationTitle, t)}</strong>
                {item.status === 'failed' ? (
                  <span className="badge text-bg-danger">{t('planning.failed')}</span>
                ) : (
                  <span className="cx-meta">{t('planning.sendsAt', { time: formatDateTime(item.sendAtUtc, i18n.language) })}</span>
                )}
                {item.fileCount > 0 && <span className="cx-meta">· {t('attachments.count', { count: item.fileCount })}</span>}
              </div>
              {editing?.id === item.id ? (
                <form
                  className="d-grid gap-2 mt-2"
                  onSubmit={(e) => {
                    e.preventDefault();
                    void act(() => planning.updateScheduled(item.id, editing.body, new Date(editing.at).toISOString()));
                  }}
                >
                  <label className="visually-hidden" htmlFor={`scheduled-body-${item.id}`}>
                    {t('messages.compose')}
                  </label>
                  <textarea id={`scheduled-body-${item.id}`} className="form-control" rows={2} value={editing.body} onChange={(e) => setEditing({ ...editing, body: e.target.value })} />
                  <label className="visually-hidden" htmlFor={`scheduled-at-${item.id}`}>
                    {t('planning.customTime')}
                  </label>
                  <input id={`scheduled-at-${item.id}`} type="datetime-local" className="form-control form-control-sm" value={editing.at} onChange={(e) => setEditing({ ...editing, at: e.target.value })} />
                  <div className="d-flex gap-2">
                    <button type="submit" className="btn btn-sm btn-primary">
                      {t('messages.save')}
                    </button>
                    <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setEditing(null)}>
                      {t('conversations.cancel')}
                    </button>
                  </div>
                </form>
              ) : (
                <>
                  <p className="cx-mention-text mb-1">{toPlainText(item.body) || t('attachments.count', { count: item.fileCount })}</p>
                  {item.status === 'failed' && (
                    <p className="text-danger small mb-1">{t('planning.failedReason', { reason: t(`errors.${item.failureCode ?? 'send_failed'}`, { defaultValue: t('planning.failedUnknown') }) })}</p>
                  )}
                  <div className="d-flex gap-3">
                    {item.status === 'pending' && (
                      <button
                        type="button"
                        className="btn btn-sm btn-link p-0"
                        onClick={() => setEditing({ id: item.id, body: item.body, at: localInputValue(new Date(item.sendAtUtc)) })}
                      >
                        {t('messages.edit')}
                      </button>
                    )}
                    <button type="button" className="btn btn-sm btn-link p-0" onClick={() => navigate(`/conversations?id=${encodeURIComponent(item.conversationId)}`)}>
                      {t('planning.openConversation')}
                    </button>
                    <button type="button" className="btn btn-sm btn-link p-0 text-danger" onClick={() => void act(() => planning.deleteScheduled(item.id))}>
                      {t(item.status === 'failed' ? 'planning.remove' : 'planning.cancelScheduled')}
                    </button>
                  </div>
                </>
              )}
            </li>
          ))}
        </ul>
      )}
    </>
  );
}

export function ReminderList() {
  const { t, i18n } = useTranslation();
  const { items, setItems, error, setError } = useLive(planning.listReminders, 'reminders');

  const done = async (id: string) => {
    try {
      await planning.deleteReminder(id);
      setItems((current) => current?.filter((r) => r.id !== id) ?? null);
    } catch (err) {
      setError(err);
    }
  };

  return (
    <>
      <p className="cx-muted">{t('planning.remindersIntro')}</p>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {items === null ? (
        error === null && <p className="cx-muted">{t('auth.working')}</p>
      ) : items.length === 0 ? (
        <p className="cx-muted mb-0">{t('planning.remindersEmpty')}</p>
      ) : (
        <ul className="list-unstyled cx-mention-list mb-0">
          {items.map((item) => (
            <li key={item.id} className="d-flex align-items-start gap-2">
              <button type="button" className={`cx-mention-item${item.status === 'due' ? ' is-unread' : ''}`} onClick={() => navigate(messageHref(item.conversationId, item.entry))}>
                {item.entry.actor && <Avatar user={item.entry.actor} size="sm" />}
                <span className="min-w-0 flex-grow-1">
                  <span className="d-flex gap-2 align-items-baseline flex-wrap">
                    <strong>{item.entry.actor?.displayName ?? t('conversations.someone')}</strong>
                    <span className="cx-meta text-truncate">{place(item.conversationType, item.conversationTitle, t)}</span>
                    <span className="cx-meta ms-auto">
                      {item.status === 'due' ? t('planning.due') : t('planning.remindsAt', { time: formatDateTime(item.remindAtUtc, i18n.language) })}
                    </span>
                  </span>
                  <span className="cx-mention-text">{toPlainText(item.entry.body)}</span>
                </span>
                {item.status === 'due' && <span className="cx-unread-dot" role="img" aria-label={t('planning.due')} />}
              </button>
              <button type="button" className="btn btn-sm btn-link mt-2 text-nowrap" onClick={() => void done(item.id)}>
                {t(item.status === 'due' ? 'planning.markDone' : 'planning.cancelReminder')}
              </button>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
