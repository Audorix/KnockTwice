import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent, type KeyboardEvent } from 'react';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { navigate, useLocation } from '../../app/router';
import type { ChannelResponse, ChannelVisibility, MyGuestInvitationResponse, OrganizationResponse, TeamResponse, WorkspaceResponse } from '../../shared/api/contracts';
import { Field, FormAlert } from '../../shared/components/Field';
import { NavIcon } from '../../shared/components/NavIcon';
import { RowMenu, type RowMenuItem } from '../../shared/components/RowMenu';
import { errorMessage } from '../auth/errors';
import { RequireUser } from '../auth/RequireUser';
import { listConversations } from '../conversations/conversationsApi';
import { listOrganizations, listWorkspaces } from '../organizations/organizationsApi';
import * as api from './businessApi';

/**
 * Channels (Phase 11; redesigned to the owner's mockup 2026-11-24): pick an organization and a
 * workspace, then browse its channels in tabs (all, joined, discover, archived) with search, a
 * visibility filter and sorting; open or join, and a "…" menu to manage, leave, archive or restore.
 * "New channel" opens a side panel. Guest invitations to channels appear on top.
 */
export function ChannelsPage() {
  return (
    <RequireUser>
      <Channels />
    </RequireUser>
  );
}

type Tab = 'all' | 'joined' | 'discover' | 'archived';
type VisibilityFilter = 'all' | ChannelVisibility;
type Sort = 'recent' | 'name' | 'members';

const tabs: Tab[] = ['all', 'joined', 'discover', 'archived'];

function Channels() {
  const { t, i18n } = useTranslation();
  const { query } = useLocation();
  const [organizations, setOrganizations] = useState<OrganizationResponse[] | null>(null);
  const [workspaces, setWorkspaces] = useState<WorkspaceResponse[]>([]);
  const [channels, setChannels] = useState<ChannelResponse[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [creating, setCreating] = useState(false);
  const [tab, setTab] = useState<Tab>('all');
  const [search, setSearch] = useState('');
  const [visibility, setVisibility] = useState<VisibilityFilter>('all');
  const [sort, setSort] = useState<Sort>('recent');
  // Without a choice in the address, open where the caller's most recently active channel is, so the
  // page does not start in an organization without channels (for example one's own new organization).
  const [recent, setRecent] = useState<{ organizationId: string; workspaceId: string } | null | undefined>(undefined);
  const fallbackOrganization = recent && organizations?.some((o) => o.id === recent.organizationId) ? recent.organizationId : organizations?.[0]?.id;
  const organizationId = query.get('org') ?? (recent === undefined ? '' : fallbackOrganization ?? '');
  const recentWorkspace = !query.get('ws') && recent && recent.organizationId === organizationId && workspaces.some((w) => w.id === recent.workspaceId)
    ? recent.workspaceId
    : undefined;
  const workspaceId = query.get('ws') ?? recentWorkspace ?? workspaces.find((w) => w.myRole)?.id ?? workspaces[0]?.id ?? '';

  useEffect(() => {
    listOrganizations().then(setOrganizations, (err: unknown) => {
      setOrganizations([]);
      setError(err);
    });
    listConversations().then((all) => {
      const latest = all
        .filter((c) => c.type === 'channel' && c.organizationId && c.workspaceId)
        .sort((a, b) => b.lastActivityAtUtc.localeCompare(a.lastActivityAtUtc))[0];
      setRecent(latest ? { organizationId: latest.organizationId!, workspaceId: latest.workspaceId! } : null);
    }, () => setRecent(null));
  }, []);

  useEffect(() => {
    setWorkspaces([]);
    if (organizationId) listWorkspaces(organizationId).then(setWorkspaces, setError);
  }, [organizationId]);

  const reload = useCallback(() => {
    if (!organizationId || !workspaceId) return;
    setChannels(null);
    api.listChannels(organizationId, workspaceId).then(setChannels, (err: unknown) => {
      setChannels([]);
      setError(err);
    });
  }, [organizationId, workspaceId]);
  useEffect(reload, [reload]);

  const select = (org: string, ws?: string) => navigate(`/channels?org=${encodeURIComponent(org)}${ws ? `&ws=${encodeURIComponent(ws)}` : ''}`, { replace: true });
  const open = (channel: ChannelResponse) => navigate(`/conversations?id=${encodeURIComponent(channel.id)}`);

  const act = async (action: () => Promise<unknown>, after?: () => void) => {
    setError(null);
    try {
      await action();
      if (after) after();
      else reload();
    } catch (err) {
      setError(err);
    }
  };

  const join = (channel: ChannelResponse) => act(() => api.joinChannel(channel.id), () => open(channel));
  const leave = (channel: ChannelResponse) => {
    if (window.confirm(t('channels.leaveConfirm', { name: channel.name }))) void act(() => api.leaveChannel(channel.id));
  };
  const archive = (channel: ChannelResponse, archived: boolean) => {
    if (!archived || window.confirm(t('channels.archiveConfirm', { name: channel.name }))) void act(() => api.setChannelArchived(channel.id, archived));
  };

  const all = useMemo(() => channels ?? [], [channels]);
  const inTab = (c: ChannelResponse, which: Tab) =>
    which === 'archived' ? !!c.archivedAtUtc : !c.archivedAtUtc && (which === 'all' || (which === 'joined' ? c.joined : !c.joined));
  const counts = Object.fromEntries(tabs.map((x) => [x, all.filter((c) => inTab(c, x)).length])) as Record<Tab, number>;
  const term = search.trim().toLocaleLowerCase();
  const visible = all
    .filter((c) => inTab(c, tab))
    .filter((c) => visibility === 'all' || c.visibility === visibility)
    .filter((c) => !term || c.name.toLocaleLowerCase().includes(term) || (c.description ?? '').toLocaleLowerCase().includes(term))
    .sort((a, b) => sort === 'name'
      ? a.name.localeCompare(b.name, i18n.language)
      : sort === 'members'
        ? b.memberCount - a.memberCount || a.name.localeCompare(b.name, i18n.language)
        : b.lastActivityAtUtc.localeCompare(a.lastActivityAtUtc));
  const workspace = workspaces.find((w) => w.id === workspaceId);

  return (
    <div className="cx-channels">
      <header className="cx-page-header">
        <div>
          <nav aria-label={t('nav.label')} className="cx-breadcrumb">
            <span>{t('nav.workspace')}</span>
            <span aria-hidden="true">/</span>
            <span aria-current="page">{t('channels.title')}</span>
          </nav>
          <h1 className="cx-page-title mb-1">{t('channels.title')}</h1>
          <p className="cx-muted mb-0">{t('channels.subtitle')}</p>
        </div>
        {organizations && organizations.length > 0 && (
          <button type="button" className="btn btn-primary cx-icon-button" onClick={() => setCreating(true)} disabled={!workspaceId || !workspace?.myRole}>
            <NavIcon name="plus" size={18} />
            {t('channels.new')}
          </button>
        )}
      </header>

      <GuestInvitations />
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}

      {organizations?.length === 0 ? (
        <section className="cx-card">
          <p className="cx-muted mb-0">{t('channels.noOrganization')}</p>
        </section>
      ) : (
        <>
          <section className="cx-card cx-scope-bar" aria-label={t('channels.scope')}>
            <NavIcon name="building" size={22} />
            <label htmlFor="channels-org" className="cx-scope-label">{t('channels.organization')}</label>
            <select id="channels-org" className="form-select" value={organizationId} onChange={(e) => select(e.target.value)}>
              {organizations?.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
            </select>
            <span className="cx-scope-divider" aria-hidden="true" />
            <label htmlFor="channels-ws" className="cx-scope-label">{t('channels.workspace')}</label>
            <select id="channels-ws" className="form-select" value={workspaceId} onChange={(e) => select(organizationId, e.target.value)}>
              {workspaces.map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
            </select>
            <span className="cx-scope-hint cx-muted">{t('channels.scopeHint')}</span>
          </section>

          <div className="cx-tabs" role="tablist" aria-label={t('channels.tabsLabel')}>
            {tabs.map((x) => (
              <button
                key={x}
                id={`channels-tab-${x}`}
                type="button"
                role="tab"
                aria-selected={tab === x}
                aria-controls="channels-panel"
                tabIndex={tab === x ? 0 : -1}
                className={`cx-tab${tab === x ? ' is-active' : ''}`}
                onClick={() => setTab(x)}
                onKeyDown={(e) => moveTab(e, x, setTab)}
              >
                {t(`channels.tabs.${x}`)}
                {(x !== 'archived' || counts.archived > 0) && channels !== null && <> <span className="cx-tab-count">{counts[x]}</span></>}
              </button>
            ))}
          </div>

          <div role="tabpanel" id="channels-panel" aria-labelledby={`channels-tab-${tab}`}>
            <div className="cx-table-tools">
              <label className="cx-search-field">
                <NavIcon name="search" size={18} />
                <span className="visually-hidden">{t('channels.findLabel')}</span>
                <input type="search" className="form-control" placeholder={t('channels.findPlaceholder')} value={search} onChange={(e) => setSearch(e.target.value)} />
              </label>
              <div className="cx-table-selects">
                <label className="visually-hidden" htmlFor="channels-visibility">{t('channels.visibilityFilter.label')}</label>
                <select id="channels-visibility" className="form-select" value={visibility} onChange={(e) => setVisibility(e.target.value as VisibilityFilter)}>
                  {(['all', 'public', 'private'] as const).map((v) => <option key={v} value={v}>{t(`channels.visibilityFilter.${v}`)}</option>)}
                </select>
                <label className="visually-hidden" htmlFor="channels-sort">{t('channels.sort.label')}</label>
                <select id="channels-sort" className="form-select" value={sort} onChange={(e) => setSort(e.target.value as Sort)}>
                  {(['recent', 'name', 'members'] as const).map((s) => <option key={s} value={s}>{t(`channels.sort.${s}`)}</option>)}
                </select>
              </div>
            </div>

            <section className="cx-card cx-table-card" aria-label={t(`channels.tabs.${tab}`)}>
              {channels === null ? (
                <p className="cx-muted cx-table-empty">{t('auth.working')}</p>
              ) : visible.length === 0 ? (
                <p className="cx-muted cx-table-empty">
                  {counts[tab] === 0 ? t(`channels.empty.${tab}`) : t('channels.noMatches')}
                </p>
              ) : (
                <table className="cx-table cx-channel-table">
                  <thead>
                    <tr>
                      <th scope="col">{t('channels.columns.channel')}</th>
                      <th scope="col">{t('channels.columns.members')}</th>
                      <th scope="col">{t('channels.columns.lastActivity')}</th>
                      <th scope="col"><span className="visually-hidden">{t('channels.columns.actions')}</span></th>
                    </tr>
                  </thead>
                  <tbody>
                    {visible.map((c) => (
                      <tr key={c.id} className={c.joined ? 'is-joined' : undefined}>
                        <td data-label={t('channels.columns.channel')}>
                          <div className="cx-channel-cell">
                            <span className={`cx-channel-icon${c.archivedAtUtc ? ' is-archived' : ''}`} aria-hidden="true">
                              <NavIcon name={c.visibility === 'private' ? 'lock' : 'hash'} size={20} />
                            </span>
                            <span className="cx-channel-text">
                              <span className="cx-channel-title">
                                <strong>{c.name}</strong>
                                <span className={`cx-tag${c.joined ? ' is-accent' : ''}`}>{t(c.joined ? 'channels.joined' : 'channels.notJoined')}</span>
                                {c.visibility === 'private' && <span className="cx-tag">{t('channels.visibility.private')}</span>}
                                {c.archivedAtUtc && <span className="cx-tag is-muted">{t('channels.archived')}</span>}
                              </span>
                              {c.description && <span className="cx-channel-description">{c.description}</span>}
                            </span>
                          </div>
                        </td>
                        <td data-label={t('channels.columns.members')}>
                          <span className="cx-channel-members" title={t('channels.members', { count: c.memberCount })}>
                            <NavIcon name="people" size={18} />
                            <span aria-hidden="true">{c.memberCount}</span>
                            <span className="visually-hidden">{t('channels.members', { count: c.memberCount })}</span>
                          </span>
                        </td>
                        <td data-label={t('channels.columns.lastActivity')}>
                          <time dateTime={c.lastActivityAtUtc} title={new Date(c.lastActivityAtUtc).toLocaleString(i18n.language)}>
                            {relativeTime(c.lastActivityAtUtc, i18n.language, t)}
                          </time>
                        </td>
                        <td className="cx-table-actions">
                          {c.joined ? (
                            <button type="button" className="btn btn-sm btn-outline-primary cx-channel-action" onClick={() => open(c)} aria-label={t('channels.openNamed', { name: c.name })}>
                              {t('channels.open')}
                            </button>
                          ) : !c.archivedAtUtc ? (
                            <button type="button" className="btn btn-sm btn-outline-primary cx-channel-action" onClick={() => void join(c)} aria-label={t('channels.joinNamed', { name: c.name })}>
                              {t('channels.joinChannel')}
                            </button>
                          ) : <span className="cx-channel-action" />}
                          <RowMenu label={t('channels.moreFor', { name: c.name })} items={menuFor(c, t, { open, join, leave, archive })} />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
              {channels !== null && (
                <div className="cx-table-footer cx-channel-footer">
                  <span className="cx-muted">{t('channels.showing', { count: visible.length })}</span>
                  <span className="cx-channel-privacy cx-muted">
                    <NavIcon name="shield" size={16} />
                    {t('channels.privacyNote')}
                  </span>
                </div>
              )}
            </section>
          </div>
        </>
      )}

      {creating && workspaceId && (
        <CreateChannelPanel organizationId={organizationId} workspaceId={workspaceId} workspaceName={workspace?.name ?? ''} onClose={() => setCreating(false)}
          onCreated={(id) => navigate(`/conversations?id=${encodeURIComponent(id)}`)} />
      )}
    </div>
  );
}

/** The "…" menu of a channel row: what the caller may do with it. */
function menuFor(c: ChannelResponse, t: TFunction, on: {
  open: (c: ChannelResponse) => void;
  join: (c: ChannelResponse) => Promise<void>;
  leave: (c: ChannelResponse) => void;
  archive: (c: ChannelResponse, archived: boolean) => void;
}): RowMenuItem[] {
  const items: RowMenuItem[] = [];
  if (c.joined) items.push({ icon: 'message', label: t('channels.open'), onSelect: () => on.open(c) });
  else if (!c.archivedAtUtc) items.push({ icon: 'plus', label: t('channels.joinChannel'), onSelect: () => void on.join(c) });
  if (c.canManage) items.push({ icon: 'settings', label: t('channels.manage'), onSelect: () => navigate(`/channel?id=${encodeURIComponent(c.id)}`) });
  if (c.canManage) {
    items.push(c.archivedAtUtc
      ? { icon: 'archive', label: t('channels.restore'), onSelect: () => on.archive(c, false), separated: true }
      : { icon: 'archive', label: t('channels.archive'), onSelect: () => on.archive(c, true), separated: true });
  }
  if (c.joined) items.push({ icon: 'leave', label: t('channels.leaveChannel'), onSelect: () => on.leave(c), danger: true, separated: !c.canManage });
  return items;
}

/** "5 min ago", "2 hours ago", "Yesterday", or a date. */
function relativeTime(value: string, language: string, t: TFunction): string {
  const date = new Date(value);
  const minutes = Math.round((Date.now() - date.getTime()) / 60_000);
  const format = new Intl.RelativeTimeFormat(language, { numeric: 'auto', style: 'long' });
  if (minutes < 1) return t('channels.justNow');
  if (minutes < 60) return format.format(-minutes, 'minute');
  if (minutes < 24 * 60 && date.toDateString() === new Date().toDateString()) return format.format(-Math.round(minutes / 60), 'hour');
  const yesterday = new Date();
  yesterday.setDate(yesterday.getDate() - 1);
  if (date.toDateString() === yesterday.toDateString()) {
    const text = format.format(-1, 'day');
    return text.charAt(0).toLocaleUpperCase(language) + text.slice(1);
  }
  return date.toLocaleDateString(language, { month: 'short', day: 'numeric', ...(date.getFullYear() !== new Date().getFullYear() ? { year: 'numeric' } : {}) });
}

/** Arrow keys move between tabs (WAI-ARIA tabs pattern). */
function moveTab(event: KeyboardEvent<HTMLButtonElement>, current: Tab, select: (tab: Tab) => void) {
  const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;
  if (step === 0) return;
  event.preventDefault();
  const next = tabs[(tabs.indexOf(current) + step + tabs.length) % tabs.length]!;
  select(next);
  document.getElementById(`channels-tab-${next}`)?.focus();
}

/** "New channel" as a side panel (modal): name, description, visibility and teams. */
function CreateChannelPanel({ organizationId, workspaceId, workspaceName, onCreated, onClose }: {
  organizationId: string;
  workspaceId: string;
  workspaceName: string;
  onCreated: (id: string) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [visibility, setVisibility] = useState<ChannelVisibility>('public');
  const [teams, setTeams] = useState<TeamResponse[]>([]);
  const [teamIds, setTeamIds] = useState<string[]>([]);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const opener = useRef<Element | null>(null);
  const first = useRef<HTMLDivElement>(null);

  useEffect(() => {
    opener.current = document.activeElement;
    first.current?.querySelector('input')?.focus();
    return () => (opener.current as HTMLElement | null)?.focus?.();
  }, []);

  useEffect(() => {
    api.listTeams(organizationId, workspaceId).then(setTeams, () => setTeams([]));
  }, [organizationId, workspaceId]);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const created = await api.createChannel(organizationId, workspaceId, { name: name.trim(), description, visibility, teamIds });
      onCreated(created.channel.id);
    } catch (err) {
      setError(err);
      setBusy(false);
    }
  };

  return (
    <div className="cx-drawer-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <aside className="cx-drawer cx-create-channel" role="dialog" aria-modal="true" aria-labelledby="new-channel-title" onKeyDown={(e) => e.key === 'Escape' && onClose()}>
        <div className="cx-drawer-header">
          <h2 id="new-channel-title" className="cx-drawer-title">{t('channels.new')}</h2>
          <button type="button" className="cx-tool" onClick={onClose} title={t('channels.closePanel')}>
            <NavIcon name="close" size={18} />
            <span className="visually-hidden">{t('channels.closePanel')}</span>
          </button>
        </div>
        <p className="cx-muted small">{t('channels.newIn', { workspace: workspaceName })}</p>
        <form onSubmit={(e) => void submit(e)} noValidate aria-label={t('channels.new')}>
          {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
          <div ref={first}>
            <Field label={t('channels.name')} maxLength={80} value={name} onChange={(e) => setName(e.target.value)} />
          </div>
          <Field label={t('channels.description')} maxLength={500} value={description} onChange={(e) => setDescription(e.target.value)} />
          <fieldset className="mb-3">
            <legend className="form-label fs-6">{t('channels.visibilityLabel')}</legend>
            {(['public', 'private'] as const).map((v) => (
              <label key={v} className={`cx-choice${visibility === v ? ' is-selected' : ''}`}>
                <input className="form-check-input" type="radio" name="visibility" checked={visibility === v} onChange={() => setVisibility(v)} />
                <NavIcon name={v === 'private' ? 'lock' : 'hash'} size={18} />
                <span>
                  <strong>{t(`channels.visibility.${v}`)}</strong>
                  <span className="cx-muted d-block small">{t(`channels.visibilityHint.${v}`)}</span>
                </span>
              </label>
            ))}
          </fieldset>
          {teams.length > 0 && (
            <fieldset className="mb-3">
              <legend className="form-label fs-6">{t('channels.addTeams')}</legend>
              {teams.map((team) => (
                <div key={team.id} className="form-check">
                  <input id={`team-${team.id}`} className="form-check-input" type="checkbox" checked={teamIds.includes(team.id)}
                    onChange={(e) => setTeamIds(e.target.checked ? [...teamIds, team.id] : teamIds.filter((x) => x !== team.id))} />
                  <label className="form-check-label" htmlFor={`team-${team.id}`}>{team.name}</label>
                </div>
              ))}
            </fieldset>
          )}
          <div className="cx-drawer-actions">
            <button type="submit" className="btn btn-primary" disabled={!name.trim() || busy}>{t('channels.create')}</button>
            <button type="button" className="btn btn-outline-secondary" onClick={onClose}>{t('conversations.cancel')}</button>
          </div>
        </form>
      </aside>
    </div>
  );
}

/** Invitations to join a channel as a guest (addressed to the signed-in user's email). */
function GuestInvitations() {
  const { t } = useTranslation();
  const [items, setItems] = useState<MyGuestInvitationResponse[]>([]);
  const [error, setError] = useState<unknown>(null);
  useEffect(() => {
    api.listMyGuestInvitations().then(setItems, () => setItems([]));
  }, []);
  if (items.length === 0) return null;

  const accept = async (id: string) => {
    try {
      const conversationId = await api.acceptGuestInvitation(id);
      navigate(`/conversations?id=${encodeURIComponent(conversationId)}`);
    } catch (err) {
      setError(err);
    }
  };
  const decline = async (id: string) => {
    try {
      await api.declineGuestInvitation(id);
      setItems((current) => current.filter((i) => i.id !== id));
    } catch (err) {
      setError(err);
    }
  };

  return (
    <section className="cx-card mb-3" aria-labelledby="guest-invitations-title">
      <h2 id="guest-invitations-title" className="cx-card-title">{t('channels.guestInvitations')}</h2>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <ul className="list-unstyled cx-session-list mb-0">
        {items.map((i) => (
          <li key={i.id} className="cx-session">
            <span>{t('channels.guestInvitation', { channel: i.channelName, organization: i.organizationName, name: i.invitedBy?.displayName ?? t('conversations.someone') })}</span>
            <span className="d-flex gap-2">
              <button type="button" className="btn btn-sm btn-primary" onClick={() => void accept(i.id)}>{t('channels.acceptGuest')}</button>
              <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => void decline(i.id)}>{t('channels.declineGuest')}</button>
            </span>
          </li>
        ))}
      </ul>
      <p className="cx-meta mt-2 mb-0">{t('channels.guestHint')}</p>
    </section>
  );
}
