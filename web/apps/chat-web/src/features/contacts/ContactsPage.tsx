import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import type { ContactRequestResponse, ContactResponse, UserSummary } from '../../shared/api/contracts';
import { Avatar } from '../../shared/components/Avatar';
import { Field, FormAlert } from '../../shared/components/Field';
import { errorMessage, fieldErrors } from '../auth/errors';
import { RequireUser, useAction } from '../auth/RequireUser';
import { useRealtime } from '../realtime/RealtimeContext';
import { openDirect } from '../conversations/conversationsApi';
import { navigate } from '../../app/router';
import * as api from './contactsApi';

/** Contacts, contact requests, user search and blocking. */
export function ContactsPage() {
  return (
    <RequireUser>
      <Contacts />
    </RequireUser>
  );
}

function Contacts() {
  const { t } = useTranslation();
  const [contacts, setContacts] = useState<ContactResponse[]>([]);
  const [requests, setRequests] = useState<ContactRequestResponse[]>([]);
  const [blocked, setBlocked] = useState<ContactResponse[]>([]);
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const reload = useCallback(() => {
    Promise.all([api.listContacts(), api.listRequests(), api.listBlocked()]).then(([c, r, b]) => {
      setContacts(c);
      setRequests(r);
      setBlocked(b);
    }, setError);
  }, []);
  useEffect(reload, [reload]);
  const act = useAction(setError, reload);

  const incoming = requests.filter((r) => r.direction === 'incoming');
  const outgoing = requests.filter((r) => r.direction === 'outgoing');

  return (
    <>
      <h1 className="cx-page-title">{t('contacts.title')}</h1>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {notice && <FormAlert tone="success">{notice}</FormAlert>}

      <UserSearch
        onRequest={(user) =>
          void act(async () => {
            const status = await api.sendRequest(user.id);
            setNotice(t(status === 'accepted' ? 'contacts.nowContacts' : 'contacts.requestSent', { name: user.displayName }));
          })
        }
      />

      {incoming.length > 0 && (
        <section className="cx-card mt-3" aria-labelledby="incoming-title">
          <h2 id="incoming-title" className="cx-card-title">
            {t('contacts.incoming')}
          </h2>
          <PeopleList
            items={incoming.map((r) => ({ key: r.id, user: r.user }))}
            actions={(item) => (
              <>
                <button type="button" className="btn btn-sm btn-primary" onClick={() => void act(() => api.acceptRequest(item.key))}>
                  {t('contacts.accept')}
                </button>
                <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void act(() => api.rejectRequest(item.key))}>
                  {t('contacts.decline')}
                </button>
              </>
            )}
          />
        </section>
      )}

      <section className="cx-card mt-3" aria-labelledby="contacts-title">
        <h2 id="contacts-title" className="cx-card-title">
          {t('contacts.list', { count: contacts.length })}
        </h2>
        {contacts.length === 0 ? (
          <p className="cx-muted mb-0">{t('contacts.empty')}</p>
        ) : (
          <PeopleList
            items={contacts.map((c) => ({ key: c.user.id, user: c.user }))}
            actions={(item) => (
              <>
                <button
                  type="button"
                  className="btn btn-sm btn-primary"
                  aria-label={t('contacts.messageNamed', { name: item.user.displayName })}
                  onClick={() =>
                    void openDirect(item.key).then(
                      (conversation) => navigate(`/conversations?id=${encodeURIComponent(conversation.id)}`),
                      setError,
                    )
                  }
                >
                  {t('contacts.message')}
                </button>
                <button
                  type="button"
                  className="btn btn-sm btn-outline-secondary"
                  aria-label={t('contacts.removeNamed', { name: item.user.displayName })}
                  onClick={() => void act(() => api.removeContact(item.key))}
                >
                  {t('contacts.remove')}
                </button>
                <button
                  type="button"
                  className="btn btn-sm btn-outline-danger"
                  aria-label={t('contacts.blockNamed', { name: item.user.displayName })}
                  onClick={() => window.confirm(t('contacts.blockConfirm', { name: item.user.displayName })) && void act(() => api.blockUser(item.key))}
                >
                  {t('contacts.block')}
                </button>
              </>
            )}
          />
        )}
      </section>

      {outgoing.length > 0 && (
        <section className="cx-card mt-3" aria-labelledby="outgoing-title">
          <h2 id="outgoing-title" className="cx-card-title">
            {t('contacts.outgoing')}
          </h2>
          <PeopleList
            items={outgoing.map((r) => ({ key: r.id, user: r.user }))}
            actions={(item) => (
              <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void act(() => api.cancelRequest(item.key))}>
                {t('contacts.cancel')}
              </button>
            )}
          />
        </section>
      )}

      {blocked.length > 0 && (
        <section className="cx-card mt-3" aria-labelledby="blocked-title">
          <h2 id="blocked-title" className="cx-card-title">
            {t('contacts.blocked')}
          </h2>
          <PeopleList
            items={blocked.map((b) => ({ key: b.user.id, user: b.user }))}
            actions={(item) => (
              <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void act(() => api.unblockUser(item.key))}>
                {t('contacts.unblock')}
              </button>
            )}
          />
        </section>
      )}
    </>
  );
}

function UserSearch({ onRequest }: { onRequest: (user: UserSummary) => void }) {
  const { t } = useTranslation();
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<UserSummary[] | null>(null);
  const [error, setError] = useState<unknown>(null);

  const search = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    try {
      setResults(await api.searchUsers(query.trim()));
    } catch (err) {
      setError(err);
      setResults(null);
    }
  };

  return (
    <section className="cx-card" aria-labelledby="search-title">
      <h2 id="search-title" className="cx-card-title">
        {t('contacts.findTitle')}
      </h2>
      <form onSubmit={search} className="cx-inline-form" role="search">
        <Field
          label={t('contacts.searchLabel')}
          type="search"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          hint={t('contacts.searchHint')}
          errors={fieldErrors(error, 'q', t)}
          className="flex-grow-1"
        />
        <button type="submit" className="btn btn-primary" disabled={query.trim().length < 2}>
          {t('contacts.search')}
        </button>
      </form>
      {error !== null && fieldErrors(error, 'q', t).length === 0 && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      {results !== null && (
        <div role="status" aria-live="polite">
          {results.length === 0 ? (
            <p className="cx-muted mb-0">{t('contacts.noResults')}</p>
          ) : (
            <PeopleList
              items={results.map((u) => ({ key: u.id, user: u }))}
              actions={(item) => (
                <button type="button" className="btn btn-sm btn-primary" onClick={() => onRequest(item.user)}>
                  {t('contacts.add')}
                </button>
              )}
            />
          )}
        </div>
      )}
    </section>
  );
}

export function PeopleList<T extends { key: string; user: UserSummary }>({
  items,
  actions,
}: {
  items: T[];
  actions?: (item: T) => React.ReactNode;
}) {
  // Presence is only known for people the user may see (contacts and colleagues).
  const { presence } = useRealtime();
  return (
    <ul className="list-unstyled mb-0 cx-session-list">
      {items.map((item) => (
        <li key={item.key} className="cx-session">
          <span className="d-flex align-items-center gap-2 min-w-0">
            <Avatar user={item.user} size="sm" presence={presence.get(item.user.id)} />
            <span className="text-truncate">{item.user.displayName}</span>
          </span>
          {actions && <span className="d-flex gap-2 flex-wrap justify-content-end">{actions(item)}</span>}
        </li>
      ))}
    </ul>
  );
}
