import { useEffect, useState } from 'react';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { navigate } from '../../app/router';
import type { ConversationDetailResponse, ConversationEntryResponse, ParticipantRole, UserSummary } from '../../shared/api/contracts';
import { Avatar } from '../../shared/components/Avatar';
import { Field } from '../../shared/components/Field';
import { useAuth } from '../auth/AuthContext';
import { listContacts } from '../contacts/contactsApi';
import { listMembers } from '../organizations/organizationsApi';
import * as api from './conversationsApi';

/** Pieces shared by the conversation list and the conversation view. */
export function useCandidates(organizationId: string) {
  const { user } = useAuth();
  const [people, setPeople] = useState<UserSummary[]>([]);
  useEffect(() => {
    let current = true;
    const load = organizationId
      ? listMembers(organizationId).then((m) => m.members.map((x) => x.user))
      : listContacts().then((c) => c.map((x) => x.user));
    load.then(
      (list) => current && setPeople(list.filter((p) => p.id !== user?.id)),
      () => current && setPeople([]),
    );
    return () => {
      current = false;
    };
  }, [organizationId, user?.id]);
  return people;
}


export function PeoplePicker({
  people,
  selected,
  multiple,
  onToggle,
  errors = [],
}: {
  people: UserSummary[];
  selected: string[];
  multiple: boolean;
  onToggle: (id: string) => void;
  errors?: string[];
}) {
  const { t } = useTranslation();
  return (
    <fieldset className="mb-3">
      <legend className="form-label fs-6">{t(multiple ? 'conversations.choosePeople' : 'conversations.choosePerson')}</legend>
      {people.length === 0 ? (
        <p className="cx-muted small mb-0">{t('conversations.noCandidates')}</p>
      ) : (
        <div className="cx-people-picker">
          {people.map((p) => (
            <label key={p.id} className="cx-choice">
              <input
                type={multiple ? 'checkbox' : 'radio'}
                name="conversation-people"
                className="form-check-input"
                checked={selected.includes(p.id)}
                onChange={() => onToggle(p.id)}
              />
              <Avatar user={p} size="sm" />
              <span className="text-truncate">{p.displayName}</span>
            </label>
          ))}
        </div>
      )}
      {errors.length > 0 && <div className="invalid-feedback d-block">{errors.join(' ')}</div>}
    </fieldset>
  );
}

export function GroupDetails({
  detail,
  canManage,
  currentUserId,
  act,
}: {
  detail: ConversationDetailResponse;
  canManage: boolean;
  currentUserId: string;
  act: (action: () => Promise<unknown>) => Promise<void>;
}) {
  const { t } = useTranslation();
  const c = detail.conversation;
  const isOwner = c.myRole === 'owner';
  const [title, setTitle] = useState(c.title);
  const [description, setDescription] = useState(detail.description ?? '');
  const [adding, setAdding] = useState<string[]>([]);
  const candidates = useCandidates(c.organizationId ?? '').filter((p) => !detail.participants.some((x) => x.user.id === p.id));

  return (
    <div className="cx-subpanel mb-3">
      {canManage && (
        <form
          className="mb-3"
          onSubmit={(e) => {
            e.preventDefault();
            void act(() => api.updateConversation(c.id, { title, description }));
          }}
        >
          <Field label={t('conversations.groupName')} maxLength={100} value={title} onChange={(e) => setTitle(e.target.value)} />
          <Field label={t('conversations.description')} maxLength={500} value={description} onChange={(e) => setDescription(e.target.value)} />
          <button type="submit" className="btn btn-sm btn-primary">
            {t('account.save')}
          </button>
        </form>
      )}

      <h3 className="h6">{t('conversations.participants', { count: detail.participants.length })}</h3>
      <ul className="list-unstyled cx-session-list">
        {detail.participants.map((p) => {
          const self = p.user.id === currentUserId;
          const canRemove = canManage && !self && (isOwner || p.role === 'member');
          return (
            <li key={p.user.id} className="cx-session">
              <span className="d-flex align-items-center gap-2 min-w-0">
                <Avatar user={p.user} size="sm" />
                <span className="text-truncate">{p.user.displayName}</span>
              </span>
              <span className="d-flex gap-2 flex-wrap justify-content-end align-items-center">
                {isOwner && !self ? (
                  <select
                    className="form-select form-select-sm w-auto"
                    aria-label={t('conversations.roleFor', { name: p.user.displayName })}
                    value={p.role}
                    onChange={(e) => void act(() => api.changeParticipantRole(c.id, p.user.id, e.target.value as ParticipantRole))}
                  >
                    {(['owner', 'administrator', 'member'] as const).map((r) => (
                      <option key={r} value={r}>
                        {t(`conversations.roles.${r}`)}
                      </option>
                    ))}
                  </select>
                ) : (
                  <span className="cx-meta">{t(`conversations.roles.${p.role}`)}</span>
                )}
                {canRemove && (
                  <button
                    type="button"
                    className="btn btn-sm btn-outline-danger"
                    aria-label={t('conversations.removeNamed', { name: p.user.displayName })}
                    onClick={() => void act(() => api.removeParticipant(c.id, p.user.id))}
                  >
                    {t('conversations.remove')}
                  </button>
                )}
              </span>
            </li>
          );
        })}
      </ul>

      {canManage && (
        <form
          className="mt-3"
          onSubmit={(e) => {
            e.preventDefault();
            void act(() => api.addParticipants(c.id, adding)).then(() => setAdding([]));
          }}
        >
          <PeoplePicker
            people={candidates}
            selected={adding}
            multiple
            onToggle={(id) => setAdding((cur) => (cur.includes(id) ? cur.filter((x) => x !== id) : [...cur, id]))}
          />
          <button type="submit" className="btn btn-sm btn-primary" disabled={adding.length === 0}>
            {t('conversations.addPeople')}
          </button>
        </form>
      )}

      <button
        type="button"
        className="btn btn-sm btn-outline-danger mt-3"
        onClick={() => {
          if (window.confirm(t('conversations.leaveConfirm', { name: c.title }))) {
            void act(() => api.leaveConversation(c.id)).then(() => navigate('/conversations'));
          }
        }}
      >
        {t('conversations.leave')}
      </button>
    </div>
  );
}

/**
 * Phase 18: a link that opens the conversation at a message (and, for a reply kept in its thread,
 * the thread).
 */
export function messageHref(conversationId: string, entry: Pick<ConversationEntryResponse, 'id' | 'threadRootId' | 'shownInConversation'>): string {
  const target = entry.threadRootId && !entry.shownInConversation ? `&thread=${encodeURIComponent(entry.threadRootId)}` : '';
  return `/conversations?id=${encodeURIComponent(conversationId)}&message=${encodeURIComponent(entry.id)}${target}`;
}

/** One readable line per history entry. Names come from the API; missing accounts show as "someone". */
export function describeEntry(entry: ConversationEntryResponse, type: 'direct' | 'group' | 'website' | 'channel', t: TFunction): string {
  const actor = entry.actor?.displayName ?? t('conversations.someone');
  const subject = entry.subject?.displayName ?? t('conversations.someone');
  switch (entry.kind) {
    case 'created':
      if (type === 'website') return t('conversations.entries.createdWebsite', { actor });
      if (type === 'channel') return t('conversations.entries.createdChannel', { actor, title: entry.text ?? '' });
      return type === 'direct' ? t('conversations.entries.createdDirect', { actor }) : t('conversations.entries.created', { actor, title: entry.text ?? '' });
    case 'member_added':
      return entry.actor && entry.actor.id === entry.subject?.id
        ? t('conversations.entries.memberJoined', { actor })
        : t('conversations.entries.memberAdded', { actor, subject });
    case 'assigned':
      return entry.subject ? t('conversations.entries.assigned', { actor, subject }) : t('conversations.entries.unassigned', { actor });
    case 'status_changed':
      return entry.text === 'closed' ? t('conversations.entries.closed', { actor }) : t('conversations.entries.reopened', { actor });
    case 'visibility_changed':
      return entry.text === 'private' ? t('conversations.entries.madePrivate', { actor }) : t('conversations.entries.madePublic', { actor });
    case 'pinned':
      return entry.replyTo?.deleted || !entry.replyTo
        ? t('conversations.entries.pinnedDeleted', { actor })
        : t('conversations.entries.pinned', { actor, excerpt: entry.replyTo.excerpt ?? '' });
    case 'member_removed':
      return t('conversations.entries.memberRemoved', { actor, subject });
    case 'member_left':
      return t('conversations.entries.memberLeft', { actor });
    case 'renamed':
      return t('conversations.entries.renamed', { actor, title: entry.text ?? '' });
    case 'description_changed':
      return entry.text ? t('conversations.entries.described', { actor }) : t('conversations.entries.descriptionCleared', { actor });
    case 'role_changed':
      return t('conversations.entries.roleChanged', { actor, subject, role: t(`conversations.roles.${entry.text ?? 'member'}`) });
    default:
      return '';
  }
}
