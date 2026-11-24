import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { Link } from '../../app/router';
import { canAdminister } from '../../app/navigationRules';
import type { ConversationSummaryResponse, OrganizationResponse, WorkspaceResponse } from '../../shared/api/contracts';
import { Avatar } from '../../shared/components/Avatar';
import { BrandMark } from '../../shared/components/BrandMark';
import { NavIcon, type NavIconName } from '../../shared/components/NavIcon';
import { supportedLanguages } from '../../i18n/i18n';
import { useAuth } from '../auth/AuthContext';
import { getMyLinks } from '../auth/authApi';
import { listRequests } from '../contacts/contactsApi';
import { listConversationPage } from '../conversations/conversationsApi';
import { useNotifications } from '../notifications/NotificationsContext';
import { listOrganizations, listWorkspaces } from '../organizations/organizationsApi';
import { listReminders, type ReminderResponse } from '../planning/planningApi';
import { useRealtime } from '../realtime/RealtimeContext';
import { ReadinessIndicator } from '../readiness/ReadinessIndicator';
import { DemoDataCard } from '../demo/DemoDataCard';

const capabilities = ['api', 'database', 'accounts', 'security', 'organizations', 'contacts', 'realtime', 'conversations', 'localization', 'docker'] as const;

/** Signed in: the welcome page (redesign 2026-11, owner mockups). Signed out: the product introduction. */
export function HomePage() {
  const { user } = useAuth();
  return user ? <Welcome /> : <Introduction />;
}

/** The signed-out landing page (redesign 2026-11, owner mockup). */
function Introduction() {
  const { t } = useTranslation();
  const { config } = useAuth();
  const features: { key: string; icon: NavIconName }[] = [
    { key: 'connected', icon: 'people' },
    { key: 'organize', icon: 'layers' },
    { key: 'share', icon: 'clip' },
    { key: 'host', icon: 'server' },
  ];
  return (
    <div className="cx-landing">
      <section className="cx-landing-hero" aria-labelledby="home-title">
        <div className="cx-landing-copy">
          <p className="cx-landing-eyebrow">{t('landing.eyebrow')}</p>
          <h1 id="home-title" className="cx-landing-title">
            <span>{t('landing.title1')}</span> <span>{t('landing.title2')}</span> <span className="is-accent">{t('landing.title3')}</span>
          </h1>
          <p className="cx-landing-lead">{t('landing.lead')}</p>
          <p className="cx-landing-actions">
            {config?.registrationEnabled !== false && <Link to="/register" className="btn btn-primary btn-lg">{t('nav.register')}</Link>}
            <Link to="/sign-in" className="btn btn-outline-primary btn-lg">{t('nav.signIn')}</Link>
          </p>
          <p className="cx-muted mb-0">{t('landing.license')}</p>
        </div>
        <ProductPreview />
      </section>

      <ul id="features" className="list-unstyled cx-landing-features" aria-label={t('landing.featuresLink')}>
        {features.map((f) => (
          <li key={f.key} className="cx-card cx-landing-feature">
            <NavIcon name={f.icon} size={36} />
            <span>
              <strong className="d-block">{t(`landing.features.${f.key}.title`)}</strong>
              <span className="cx-muted">{t(`landing.features.${f.key}.text`)}</span>
            </span>
          </li>
        ))}
      </ul>

      <ReadinessIndicator compact />

      <details className="cx-card cx-landing-included">
        <summary>
          <span>{t('landing.included', { stage: t('app.stage') })}</span>
          <NavIcon name="chevron-down" size={20} />
        </summary>
        <BuildCard />
      </details>
    </div>
  );
}

/** A still picture of the app for the landing page (decorative; the text says what it does). */
function ProductPreview() {
  const { t } = useTranslation();
  const people = [
    { initials: 'JM', name: 'Jamie', time: '10:14', text: t('landing.preview.m1') },
    { initials: 'RK', name: 'Riley', time: '10:18', text: t('landing.preview.m2') },
    { initials: 'AS', name: 'Avery', time: '10:24', text: t('landing.preview.m3') },
  ];
  return (
    <div className="cx-landing-preview" aria-hidden="true">
      <span className="cx-landing-blob is-a" />
      <span className="cx-landing-blob is-b" />
      <div className="cx-mock">
        <div className="cx-mock-rail">
          <NavIcon name="chats" size={18} />
          <NavIcon name="people" size={18} />
          <NavIcon name="clip" size={18} />
          <NavIcon name="server" size={18} />
          <span className="cx-mock-me">AS</span>
        </div>
        <div className="cx-mock-side">
          <div className="cx-mock-brand"><BrandMark size={22} /> {t('app.name')}</div>
          <div className="cx-mock-search"><NavIcon name="search" size={13} /> {t('landing.preview.search')}</div>
          <div className="cx-mock-group">{t('landing.preview.direct')} <NavIcon name="chevron-down" size={12} /></div>
          <div className="cx-mock-item"><span className="cx-mock-avatar">JM</span> Jamie</div>
          <div className="cx-mock-item"><span className="cx-mock-avatar">RK</span> Riley</div>
          <div className="cx-mock-group">{t('landing.preview.channels')} <NavIcon name="chevron-down" size={12} /></div>
          {['design-team', 'engineering', 'product', 'general'].map((name, i) => (
            <div key={name} className={`cx-mock-item${i === 0 ? ' is-active' : ''}`}># {name}</div>
          ))}
        </div>
        <div className="cx-mock-chat">
          <div className="cx-mock-head"># design-team <span><NavIcon name="people" size={16} /> <NavIcon name="dots" size={16} /></span></div>
          {people.map((p) => (
            <div key={p.initials} className="cx-mock-message">
              <span className="cx-mock-avatar is-large">{p.initials}</span>
              <div>
                <div><strong>{p.name}</strong> <span className="cx-mock-time">{p.time}</span></div>
                <div>{p.text}</div>
              </div>
            </div>
          ))}
          <div className="cx-mock-file">
            <NavIcon name="file" size={20} />
            <span><strong>Project Design v3.pdf</strong><br /><span className="cx-mock-time">2.4 MB · PDF</span></span>
            <NavIcon name="download" size={16} />
          </div>
          <div className="cx-mock-composer">
            <NavIcon name="plus" size={16} />
            <span>{t('landing.preview.compose')}</span>
            <NavIcon name="smile" size={16} />
            <span className="cx-mock-send"><NavIcon name="send" size={14} /></span>
          </div>
        </div>
      </div>
    </div>
  );
}

interface WorkspaceRow {
  organization: OrganizationResponse;
  workspace: WorkspaceResponse;
}

function Welcome() {
  const { t, i18n } = useTranslation();
  const { user, config } = useAuth();
  const { status } = useRealtime();
  const { mentions } = useNotifications();
  const [recent, setRecent] = useState<ConversationSummaryResponse[] | null>(null);
  const [requests, setRequests] = useState(0);
  const [reminder, setReminder] = useState<ReminderResponse | null>(null);
  const [workspaces, setWorkspaces] = useState<WorkspaceRow[] | null>(null);
  const [hasLinks, setHasLinks] = useState(true);
  const [toolsOpen, setToolsOpen] = useState(false);

  useEffect(() => {
    listConversationPage(null, 3).then((page) => setRecent(page.items.slice(0, 3)), () => setRecent([]));
    listRequests().then((all) => setRequests(all.filter((r) => r.direction === 'incoming').length), () => undefined);
    listReminders().then(
      (all) => setReminder([...all].sort((a, b) => a.remindAtUtc.localeCompare(b.remindAtUtc))[0] ?? null),
      () => undefined,
    );
    getMyLinks().then((links) => setHasLinks(links.length > 0), () => undefined);
    listOrganizations().then(
      async (organizations) => {
        const rows: WorkspaceRow[] = [];
        for (const organization of organizations.slice(0, 4)) {
          const list = await listWorkspaces(organization.id).catch(() => [] as WorkspaceResponse[]);
          for (const workspace of list.filter((w) => w.myRole)) rows.push({ organization, workspace });
        }
        setWorkspaces(rows.slice(0, 4));
      },
      () => setWorkspaces([]),
    );
  }, []);

  if (!user) return null;
  const admin = canAdminister(user.permissions);
  const development = !!config?.demoDataEnabled;
  const today = new Date().toLocaleDateString(i18n.language, { weekday: 'long', month: 'long', day: 'numeric' });
  const connected = status === 'connected';

  return (
    <div className="cx-home">
      <header className="cx-home-top">
        <nav aria-label={t('nav.label')} className="cx-breadcrumb mb-0">
          <span>{t('nav.workspace')}</span>
          <span aria-hidden="true">/</span>
          <span aria-current="page">{t('nav.homeItem')}</span>
        </nav>
        <div className="cx-home-status">
          <span>{today}</span>
          {admin && development && (
            <>
              <span className="cx-home-divider" aria-hidden="true" />
              <button type="button" className="btn btn-outline-primary btn-sm cx-icon-button" onClick={() => setToolsOpen(true)}>
                <NavIcon name="tools" size={16} />
                {t('home.tools.title')}
                <span className="cx-home-dev">{t('home.tools.dev')}</span>
              </button>
            </>
          )}
          <span className="cx-home-divider" aria-hidden="true" />
          <span className="cx-home-connection">
            <span className={`cx-dot cx-dot-${connected ? 'ok' : status === 'disconnected' ? 'down' : 'warn'}`} aria-hidden="true" />
            {t(`home.connection.${status}`)}
          </span>
        </div>
      </header>

      <section className="cx-home-hero" aria-labelledby="home-title">
        <div>
          <h1 id="home-title" className="cx-home-title">{t('home.welcomeBack', { name: user.displayName })}</h1>
          <p className="cx-home-intro">{t('home.intro')}</p>
          <div className="cx-home-actions">
            <Link to="/conversations" className="btn btn-primary">{t('home.openChats')}</Link>
            <Link to="/conversations?new=1" className="btn btn-outline-light cx-icon-button">
              <NavIcon name="plus" size={18} />
              {t('home.newConversation')}
            </Link>
          </div>
        </div>
        <HeroArt />
      </section>

      <ul className="list-unstyled cx-home-shortcuts" aria-label={t('home.shortcuts')}>
        <Shortcut to="/contacts" icon="people" title={t('home.find.title')} text={t('home.find.text')} />
        <Shortcut to="/channels" icon="hash" title={t('home.channels.title')} text={t('home.channels.text')} />
        <Shortcut to="/mentions?tab=saved" icon="bookmark" title={t('home.saved.title')} text={t('home.saved.text')} />
        <Shortcut to="/account" icon="user" title={t('home.profile.title')} text={t('home.profile.text')} />
      </ul>

      <div className="cx-home-grid">
        <div className="cx-home-column">
          <section className="cx-card cx-home-card" aria-labelledby="home-recent-title">
            <div className="cx-home-card-head">
              <h2 id="home-recent-title" className="cx-card-title mb-0">{t('home.recent.title')}</h2>
              <Link to="/conversations" className="cx-home-more">{t('home.recent.viewAll')}<NavIcon name="arrow-right" size={16} /></Link>
            </div>
            {recent === null ? (
              <p className="cx-muted mb-0">{t('auth.working')}</p>
            ) : recent.length === 0 ? (
              <p className="cx-muted mb-0">{t('home.recent.empty')}</p>
            ) : (
              <ul className="list-unstyled mb-0 cx-home-recent">
                {recent.map((c) => <RecentRow key={c.id} conversation={c} me={user.id} />)}
              </ul>
            )}
          </section>

          <section className="cx-card cx-home-card" aria-labelledby="home-workspaces-title">
            <h2 id="home-workspaces-title" className="cx-card-title">{t('home.workspaces.title')}</h2>
            {workspaces === null ? (
              <p className="cx-muted mb-0">{t('auth.working')}</p>
            ) : workspaces.length === 0 ? (
              <p className="cx-muted mb-0">
                {t('home.workspaces.empty')} <Link to="/organizations">{t('home.workspaces.organizations')}</Link>
              </p>
            ) : (
              <ul className="list-unstyled mb-0 cx-home-workspaces">
                {workspaces.map(({ organization, workspace }) => (
                  <li key={workspace.id}>
                    <span className="cx-home-tile" aria-hidden="true"><NavIcon name="building" size={20} /></span>
                    <strong className="cx-home-workspace-org">{organization.name}</strong>
                    <span className="cx-home-workspace-name">{workspace.name}</span>
                    <Link to={`/channels?org=${encodeURIComponent(organization.id)}&ws=${encodeURIComponent(workspace.id)}`} className="cx-home-more"
                      aria-label={t('home.workspaces.openNamed', { organization: organization.name, workspace: workspace.name })}>
                      {t('home.workspaces.open')}<NavIcon name="arrow-right" size={16} />
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </div>

        <div className="cx-home-column">
          <section className="cx-card cx-home-card" aria-labelledby="home-attention-title">
            <h2 id="home-attention-title" className="cx-card-title">{t('home.attention.title')}</h2>
            <ul className="list-unstyled cx-home-attention">
              <li>
                <NavIcon name="at" size={22} />
                <strong>{t('home.attention.mentions', { count: mentions })}</strong>
                <Link to="/mentions" className="cx-home-more">{t('home.attention.view')}<NavIcon name="arrow-right" size={16} /></Link>
              </li>
              <li>
                <NavIcon name="user-plus" size={22} />
                <strong>{t('home.attention.requests', { count: requests })}</strong>
                <Link to="/contacts?tab=requests" className="cx-home-more">{t('home.attention.review')}<NavIcon name="arrow-right" size={16} /></Link>
              </li>
            </ul>
            <div className="cx-home-reminder">
              <NavIcon name="clock" size={22} />
              {reminder ? (
                <div className="min-w-0">
                  <span className="cx-muted small">{t(reminder.status === 'due' ? 'home.attention.dueReminder' : 'home.attention.nextReminder')}</span>
                  <strong className="d-block text-truncate">{reminderText(reminder, t)}</strong>
                  <span className="cx-muted small">
                    {when(reminder.remindAtUtc, t, i18n.language)}
                    {reminder.conversationTitle && <> · {reminder.conversationType === 'channel' ? `# ${reminder.conversationTitle}` : reminder.conversationTitle}</>}
                  </span>
                  <Link to="/mentions?tab=reminders" className="cx-home-more d-flex mt-1">{t('home.attention.openReminder')}<NavIcon name="arrow-right" size={16} /></Link>
                </div>
              ) : (
                <span className="cx-muted">{t('home.attention.noReminders')}</span>
              )}
            </div>
          </section>

          {(!user.avatar.photoUrl || !hasLinks) && (
            <section className="cx-card cx-home-card cx-home-yours" aria-labelledby="home-yours-title">
              <NavIcon name="camera" size={26} />
              <div>
                <h2 id="home-yours-title" className="cx-card-title mb-1">{t('home.yours.title')}</h2>
                <p className="cx-muted mb-2">{t('home.yours.text')}</p>
                <Link to="/account" className="cx-home-more">{t('home.yours.edit')}<NavIcon name="arrow-right" size={16} /></Link>
              </div>
            </section>
          )}
        </div>
      </div>

      {admin && (
        <button type="button" className="cx-card cx-home-admin" onClick={() => setToolsOpen(true)}>
          <NavIcon name="tools" size={28} />
          <span className="flex-grow-1 text-start">
            <span className="cx-home-admin-title">
              {t('home.admin.title')} <span className="cx-home-badge">{t('home.admin.badge')}</span>
            </span>
            <span className="cx-muted d-block">{t('home.admin.text')}</span>
          </span>
          <NavIcon name="chevron-right" size={22} />
        </button>
      )}

      {toolsOpen && <DevelopmentTools development={development} onClose={() => setToolsOpen(false)} />}
    </div>
  );
}

function Shortcut({ to, icon, title, text }: { to: string; icon: NavIconName; title: string; text: string }) {
  return (
    <li>
      <Link to={to} className="cx-card cx-home-shortcut">
        <NavIcon name={icon} size={28} />
        <span>
          <strong className="d-block">{title}</strong>
          <span className="cx-muted">{text}</span>
        </span>
      </Link>
    </li>
  );
}

function RecentRow({ conversation: c, me }: { conversation: ConversationSummaryResponse; me: string }) {
  const { t, i18n } = useTranslation();
  const channel = c.type === 'channel';
  const subtitle = c.type === 'direct' ? t('home.recent.direct') : c.organizationName ?? t(`conversations.types.${c.type}`, { defaultValue: '' });
  const last = c.lastMessage;
  const author = last?.author && last.author.id !== me && c.type !== 'direct' ? `${last.author.displayName.split(' ')[0]}: ` : '';
  const excerpt = last ? (last.deleted ? t('messages.deleted') : last.excerpt || t('home.recent.files')) : '';
  const at = last?.createdAtUtc ?? c.lastActivityAtUtc;
  return (
    <li>
      <Link to={`/conversations?id=${encodeURIComponent(c.id)}`} className="cx-home-recent-row">
        {c.type === 'direct' && c.counterpart ? (
          <Avatar user={c.counterpart} />
        ) : (
          <span className="cx-home-tile is-round" aria-hidden="true">{channel ? '#' : c.title.slice(0, 1).toUpperCase()}</span>
        )}
        <span className="cx-home-recent-who">
          <strong className="d-block text-truncate">{channel ? `# ${c.title}` : c.title}</strong>
          <span className="cx-muted small">{subtitle}</span>
        </span>
        <span className="cx-home-recent-text">{author}{excerpt}</span>
        <time className="cx-muted small" dateTime={at}>{shortTime(at, t, i18n.language)}</time>
        {c.unreadCount > 0 ? (
          <span className="cx-home-unread" aria-label={t('notifications.unread', { count: c.unreadCount })}>{c.unreadCount > 99 ? '99+' : c.unreadCount}</span>
        ) : (
          <span />
        )}
      </Link>
    </li>
  );
}

/** Development tools (owner mockup): service status, demo data and what this build contains. */
function DevelopmentTools({ development, onClose }: { development: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const close = useRef<HTMLButtonElement>(null);
  const opener = useRef<Element | null>(null);

  useEffect(() => {
    opener.current = document.activeElement;
    close.current?.focus();
    return () => (opener.current as HTMLElement | null)?.focus?.();
  }, []);

  return (
    <div className="cx-drawer-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <aside className="cx-drawer cx-tools-drawer" role="dialog" aria-modal="true" aria-labelledby="tools-title" onKeyDown={(e) => e.key === 'Escape' && onClose()}>
        <div className="cx-tools-head">
          <NavIcon name="tools" size={26} />
          <div className="flex-grow-1">
            <h2 id="tools-title" className="cx-tools-title">
              {t('home.tools.title')} {development && <span className="cx-home-badge is-warn">{t('home.tools.developmentOnly')}</span>}
            </h2>
            <p className="cx-muted mb-0">{t('home.tools.subtitle')}</p>
          </div>
          <button ref={close} type="button" className="cx-tool" onClick={onClose} aria-label={t('home.tools.close')}>
            <NavIcon name="close" size={20} />
          </button>
        </div>
        <div className="cx-tools-body">
          <ReadinessIndicator />
          {development && <DemoDataCard />}
          <BuildCard />
        </div>
        {development && (
          <p className="cx-tools-foot">
            <NavIcon name="lock" size={18} />
            {t('home.tools.footer')}
          </p>
        )}
      </aside>
    </div>
  );
}

function BuildCard() {
  const { t } = useTranslation();
  return (
    <section className="cx-card" aria-labelledby="included-title">
      <div className="cx-card-header">
        <h2 id="included-title" className="cx-card-title">{t('foundation.includedTitle')}</h2>
        <span className="cx-muted small">
          {t('home.build.summary', { capabilities: capabilities.length, languages: t('home.build.languages', { count: supportedLanguages.length }) })}
        </span>
      </div>
      <ul className="list-unstyled cx-build-list mb-0">
        {capabilities.map((item) => (
          <li key={item}>
            <NavIcon name="check" size={18} />
            <strong>{t(`home.build.items.${item}.name`)}</strong>
            <span className="cx-muted">— {t(`home.build.items.${item}.text`)}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}

/** Decorative speech bubbles and soft curves, as in the mockup. */
function HeroArt() {
  return (
    <svg className="cx-home-art" viewBox="0 0 420 170" aria-hidden="true" focusable="false">
      <path d="M0 150C90 150 120 60 210 95s150 10 210-70" fill="none" stroke="currentColor" strokeOpacity=".35" strokeWidth="1.5" />
      <path d="M40 165c80-10 140-90 230-60s110-20 150-60" fill="none" stroke="currentColor" strokeOpacity=".2" strokeWidth="1.5" />
      <rect x="218" y="22" width="110" height="76" rx="16" fill="none" stroke="currentColor" strokeWidth="3" />
      <path d="M240 98l-12 22 30-22" fill="none" stroke="currentColor" strokeWidth="3" strokeLinejoin="round" />
      <rect x="270" y="52" width="116" height="80" rx="16" fill="var(--cx-surface)" stroke="currentColor" strokeWidth="3" />
      <path d="M352 132l14 20-34-20" fill="var(--cx-surface)" stroke="currentColor" strokeWidth="3" strokeLinejoin="round" />
    </svg>
  );
}

// ---- Helpers --------------------------------------------------------------------------------

function sameDay(a: Date, b: Date) {
  return a.toDateString() === b.toDateString();
}

/** "10:42", "Yesterday", or the date. */
function shortTime(iso: string, t: TFunction, language: string): string {
  const date = new Date(iso);
  const now = new Date();
  if (sameDay(date, now)) return date.toLocaleTimeString(language, { hour: '2-digit', minute: '2-digit' });
  if (sameDay(date, new Date(now.getFullYear(), now.getMonth(), now.getDate() - 1))) return t('home.yesterday');
  return date.toLocaleDateString(language, { day: 'numeric', month: 'short' });
}

/** "Today, 14:00", "Tomorrow, 09:00", or the date and time. */
function when(iso: string, t: TFunction, language: string): string {
  const date = new Date(iso);
  const now = new Date();
  const time = date.toLocaleTimeString(language, { hour: '2-digit', minute: '2-digit' });
  if (sameDay(date, now)) return t('home.todayAt', { time });
  if (sameDay(date, new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1))) return t('home.tomorrowAt', { time });
  return `${date.toLocaleDateString(language, { weekday: 'short', day: 'numeric', month: 'short' })}, ${time}`;
}

function reminderText(reminder: ReminderResponse, t: TFunction): string {
  const body = reminder.entry.body?.replace(/[*_~`>#[\]()]/g, '').trim();
  if (!body) return t('home.attention.reminderMessage');
  return body.length > 80 ? `${body.slice(0, 79)}…` : body;
}
