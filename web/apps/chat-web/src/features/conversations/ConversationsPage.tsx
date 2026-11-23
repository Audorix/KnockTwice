import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { navigate, useLocation } from '../../app/router';
import type { ConversationSummaryResponse, OrganizationResponse } from '../../shared/api/contracts';
import { Avatar } from '../../shared/components/Avatar';
import { Field, FormAlert } from '../../shared/components/Field';
import { errorMessage, fieldErrors } from '../auth/errors';
import { RequireUser } from '../auth/RequireUser';
import { listOrganizations } from '../organizations/organizationsApi';
import { useRealtime } from '../realtime/RealtimeContext';
import * as api from './conversationsApi';
import { listDrafts } from '../planning/planningApi';
import { PeoplePicker, useCandidates } from './conversationParts';
import { ConversationView } from './ConversationView';

export { describeEntry } from './conversationParts';

const pageSize = 50;
const maxPageSize = 200; // the server's largest page

/** Direct and group conversations (Phase 5). Messages arrive in Phase 6; history shows conversation events. */
export function ConversationsPage() {
  return (
    <RequireUser>
      <div className="cx-conversations-host">
        <Conversations />
      </div>
    </RequireUser>
  );
}

function Conversations() {
  const { t } = useTranslation();
  const { query } = useLocation();
  const { client } = useRealtime();
  const selectedId = query.get('id');
  const [items, setItems] = useState<ConversationSummaryResponse[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  const [drafts, setDrafts] = useState<Set<string>>(new Set());
  // Phase 22: the list comes a page at a time. Refreshes fetch as many as are shown, so the list
  // does not shrink back to one page when a message arrives.
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loadingMore, setLoadingMore] = useState(false);
  const shown = useRef(pageSize);

  const reload = useCallback(() => {
    void (async () => {
      let list: ConversationSummaryResponse[] = [];
      let cursor: string | null = null;
      let full = true;
      do {
        const size = Math.min(maxPageSize, shown.current - list.length);
        const page = await api.listConversationPage(cursor, size);
        list = [...list, ...page.items.filter((i) => !list.some((x) => x.id === i.id))];
        cursor = page.hasMore ? page.nextCursor : null;
        full = page.items.length >= size;
      } while (cursor && full && list.length < shown.current);
      setItems(list);
      setNextCursor(cursor);
    })().catch(setError);
    // Phase 20: conversations with an unsent draft are marked.
    listDrafts().then((all) => setDrafts(new Set(all.filter((d) => !d.threadRootId).map((d) => d.conversationId))), () => undefined);
  }, []);

  useEffect(reload, [reload]);

  // Keep the list current: any change to one of the user's conversations, and after reconnects.
  useEffect(() => {
    if (!client) return;
    const offUpdate = client.onConversationUpdated((event) => {
      reload();
      if (event.change === 'removed' && event.conversationId === new URLSearchParams(window.location.search).get('id')) {
        setNotice(t('conversations.removedNotice'));
        navigate('/conversations', { replace: true });
      }
    });
    const offReconnect = client.onReconnected(reload);
    // New messages change order and unread counts; coalesce bursts into one reload.
    let pending: ReturnType<typeof setTimeout> | undefined;
    const offMessage = client.onMessage(() => {
      if (pending) return;
      pending = setTimeout(() => {
        pending = undefined;
        reload();
      }, 300);
    });
    return () => {
      clearTimeout(pending);
      offUpdate();
      offReconnect();
      offMessage();
    };
  }, [client, reload, t]);

  const loadMore = async () => {
    if (!nextCursor) return;
    setLoadingMore(true);
    try {
      const page = await api.listConversationPage(nextCursor, pageSize);
      setItems((current) => {
        const list = current ?? [];
        const merged = [...list, ...page.items.filter((i) => !list.some((x) => x.id === i.id))];
        shown.current = Math.max(shown.current, merged.length);
        return merged;
      });
      setNextCursor(page.hasMore ? page.nextCursor : null);
    } catch (err) {
      setError(err);
    } finally {
      setLoadingMore(false);
    }
  };

  const open = (id: string) => {
    setNotice(null);
    setCreating(false);
    navigate(`/conversations?id=${encodeURIComponent(id)}`);
  };

  return (
    <div className={`cx-conversations${selectedId || creating ? ' has-selection' : ''}`}>
      <section className="cx-card cx-conversation-list" aria-labelledby="conversations-title">
        <div className="cx-card-header">
          <h1 id="conversations-title" className="cx-card-title">
            {t('conversations.title')}
          </h1>
          <button type="button" className="btn btn-sm btn-primary" onClick={() => setCreating((c) => !c)} aria-expanded={creating}>
            {t('conversations.new')}
          </button>
        </div>
        {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
        {notice && <FormAlert tone="info">{notice}</FormAlert>}
        {items === null ? (
          <p className="cx-muted">{t('auth.working')}</p>
        ) : items.length === 0 ? (
          <p className="cx-muted mb-0">{t('conversations.empty')}</p>
        ) : (
          <ul className="list-unstyled mb-0">
            {items.map((item) => (
              <li key={item.id}>
                <button
                  type="button"
                  className={`cx-conversation-item${item.id === selectedId ? ' is-active' : ''}`}
                  aria-current={item.id === selectedId ? 'true' : undefined}
                  onClick={() => open(item.id)}
                >
                  <ConversationAvatar item={item} />
                  <span className="cx-conversation-text">
                    <span className="cx-conversation-name">{item.title || t('conversations.unknownPerson')}</span>
                    <span className="cx-conversation-meta">
                      {drafts.has(item.id) && item.id !== selectedId && <span className="cx-draft-mark">{t('planning.draft')} · </span>}
                      {item.type === 'website' && <span className="badge cx-website-badge me-1">{t('conversations.types.website')}</span>}
                      {item.scope === 'organization' ? item.organizationName : t(`conversations.types.${item.type}`)}
                      {item.muted && ` · ${t(item.notificationLevel === 'nothing' ? 'conversations.muted' : 'conversations.mentionsOnly')}`}
                    </span>
                  </span>
                  {item.unreadCount > 0 && (
                    <span className="badge rounded-pill text-bg-primary" aria-label={t('conversations.unread', { count: item.unreadCount })}>
                      {item.unreadCount}
                    </span>
                  )}
                </button>
              </li>
            ))}
          </ul>
        )}
        {nextCursor && (
          <button type="button" className="btn btn-sm btn-outline-secondary w-100 mt-2" onClick={() => void loadMore()} disabled={loadingMore}>
            {loadingMore ? t('auth.working') : t('conversations.loadMoreConversations')}
          </button>
        )}
      </section>

      <div className="cx-conversation-main">
        {creating ? (
          <NewConversation onCreated={(id) => { reload(); open(id); }} onCancel={() => setCreating(false)} />
        ) : selectedId ? (
          <ConversationView key={selectedId} conversationId={selectedId} onChanged={reload} />
        ) : (
          <section className="cx-card">
            <p className="cx-muted mb-0">{t('conversations.pick')}</p>
          </section>
        )}
      </div>
    </div>
  );
}

function ConversationAvatar({ item }: { item: ConversationSummaryResponse }) {
  const { presence } = useRealtime();
  if (item.counterpart) return <Avatar user={item.counterpart} size="sm" presence={presence.get(item.counterpart.id)} />;
  const initials = item.title.split(' ').filter(Boolean).slice(0, 2).map((w) => w[0]!.toUpperCase()).join('');
  return <Avatar user={{ id: item.id, avatar: { initials } }} size="sm" />;
}

function NewConversation({ onCreated, onCancel }: { onCreated: (id: string) => void; onCancel: () => void }) {
  const { t } = useTranslation();
  const [kind, setKind] = useState<'direct' | 'group'>('direct');
  const [organizations, setOrganizations] = useState<OrganizationResponse[]>([]);
  const [organizationId, setOrganizationId] = useState('');
  const [title, setTitle] = useState('');
  const [selected, setSelected] = useState<string[]>([]);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const people = useCandidates(organizationId);

  useEffect(() => {
    listOrganizations().then(setOrganizations, () => setOrganizations([]));
  }, []);

  const toggle = (id: string) =>
    setSelected((current) => (kind === 'direct' ? [id] : current.includes(id) ? current.filter((x) => x !== id) : [...current, id]));

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const org = organizationId || undefined;
      const created = kind === 'direct' ? await api.openDirect(selected[0]!, org) : await api.createGroup(title, selected, org);
      onCreated(created.id);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="cx-card" aria-labelledby="new-conversation-title">
      <h2 id="new-conversation-title" className="cx-card-title">
        {t('conversations.new')}
      </h2>
      {error !== null && fieldErrors(error, 'title', t).length === 0 && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <form onSubmit={submit} noValidate>
        <fieldset className="mb-3">
          <legend className="form-label fs-6">{t('conversations.kind')}</legend>
          <div className="btn-group" role="group">
            {(['direct', 'group'] as const).map((k) => (
              <button
                key={k}
                type="button"
                className={`btn btn-sm ${kind === k ? 'btn-primary' : 'btn-outline-primary'}`}
                aria-pressed={kind === k}
                onClick={() => {
                  setKind(k);
                  setSelected([]);
                }}
              >
                {t(`conversations.types.${k}`)}
              </button>
            ))}
          </div>
        </fieldset>
        <div className="mb-3">
          <label htmlFor="conversation-scope" className="form-label">
            {t('conversations.scope')}
          </label>
          <select
            id="conversation-scope"
            className="form-select"
            value={organizationId}
            onChange={(e) => {
              setOrganizationId(e.target.value);
              setSelected([]);
            }}
          >
            <option value="">{t('conversations.personal')}</option>
            {organizations.map((o) => (
              <option key={o.id} value={o.id}>
                {o.name}
              </option>
            ))}
          </select>
          <div className="form-text">{t(organizationId ? 'conversations.scopeOrganizationHint' : 'conversations.scopePersonalHint')}</div>
        </div>
        {kind === 'group' && (
          <Field label={t('conversations.groupName')} required maxLength={100} value={title} onChange={(e) => setTitle(e.target.value)} errors={fieldErrors(error, 'title', t)} />
        )}
        <PeoplePicker people={people} selected={selected} multiple={kind === 'group'} onToggle={toggle} errors={fieldErrors(error, 'userIds', t)} />
        <div className="d-flex gap-2">
          <button type="submit" className="btn btn-primary" disabled={busy || selected.length === 0 || (kind === 'group' && !title.trim())}>
            {busy ? t('auth.working') : t(kind === 'direct' ? 'conversations.openDirect' : 'conversations.createGroup')}
          </button>
          <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>
            {t('conversations.cancel')}
          </button>
        </div>
      </form>
    </section>
  );
}

