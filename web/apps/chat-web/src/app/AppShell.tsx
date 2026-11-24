import { lazy, Suspense, useEffect, useRef, useState, type ComponentType } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useLocation } from './router';
import { useAuth } from '../features/auth/AuthContext';
import { LanguageSelector } from '../features/language/LanguageSelector';
import { BrandMark } from '../shared/components/BrandMark';
import { HomePage } from '../features/foundation/HomePage';
import {
  ForgotPasswordPage,
  RegisterPage,
  ResendConfirmationPage,
  ResetPasswordPage,
  SignInPage,
  VerifyEmailPage,
} from '../features/auth/AuthPages';
import { Avatar } from '../shared/components/Avatar';
import { RealtimeIndicator } from '../features/realtime/RealtimeIndicator';
import { ConversationsPage } from '../features/conversations/ConversationsPage';
import { PrivateChatInvites } from '../features/private/PrivateChatInvites';
import { NotificationToasts, useNotifications } from '../features/notifications/NotificationsContext';
import { NavIcon, type NavIconName } from '../shared/components/NavIcon';
import { listOrganizations } from '../features/organizations/organizationsApi';
import { canAdminister, supportOrganizations } from './navigationRules';

/**
 * Phase 22: pages other than the chat and the sign-in flow are loaded on demand, so the first
 * download stays small (see scripts/check-bundle.mjs for the budget).
 */
const page = <K extends string>(load: () => Promise<Record<K, ComponentType>>, name: K) =>
  lazy(async () => ({ default: (await load())[name] }));
const AccountPage = page(() => import('../features/account/AccountPage'), 'AccountPage');
const ContactsPage = page(() => import('../features/contacts/ContactsPage'), 'ContactsPage');
const OrganizationsPage = page(() => import('../features/organizations/OrganizationsPage'), 'OrganizationsPage');
const OrganizationPage = page(() => import('../features/organizations/OrganizationPage'), 'OrganizationPage');
const PrivateChatPage = page(() => import('../features/private/PrivateChatPage'), 'PrivateChatPage');
const AdminPage = page(() => import('../features/admin/AdminPage'), 'AdminPage');
const ChannelsPage = page(() => import('../features/business/ChannelsPage'), 'ChannelsPage');
const ChannelPage = page(() => import('../features/business/ChannelPage'), 'ChannelPage');
const SupportPage = page(() => import('../features/business/SupportPage'), 'SupportPage');
const SearchPage = page(() => import('../features/search/SearchPage'), 'SearchPage');
const MentionsPage = page(() => import('../features/mentions/MentionsPage'), 'MentionsPage');

const routes: Record<string, ComponentType> = {
  '/': HomePage,
  '/sign-in': SignInPage,
  '/register': RegisterPage,
  '/forgot-password': ForgotPasswordPage,
  '/resend-confirmation': ResendConfirmationPage,
  '/reset-password': ResetPasswordPage,
  '/verify-email': VerifyEmailPage,
  '/account': AccountPage,
  '/contacts': ContactsPage,
  '/organizations': OrganizationsPage,
  '/organization': OrganizationPage,
  '/conversations': ConversationsPage,
  '/private': PrivateChatPage,
  '/admin': AdminPage,
  '/channels': ChannelsPage,
  '/channel': ChannelPage,
  '/support': SupportPage,
  '/search': SearchPage,
  '/mentions': MentionsPage,
};

/**
 * Two layouts (Phase 8, owner decision): signed-out pages keep the identity panel; signed-in
 * users get a chat-first shell with a navigation rail (a bottom tab bar on small screens).
 */
export function AppShell() {
  const { t } = useTranslation();
  const { pathname } = useLocation();
  const { user } = useAuth();
  const main = useRef<HTMLElement>(null);
  const Page = routes[pathname] ?? HomePage;

  // Move focus to the new page for keyboard and screen-reader users after navigation.
  const firstRender = useRef(true);
  useEffect(() => {
    if (firstRender.current) {
      firstRender.current = false;
      return;
    }
    // Without scrolling: the signed-out top bar sits above <main> and must stay in view.
    main.current?.focus({ preventScroll: true });
    window.scrollTo?.(0, 0);
  }, [pathname]);

  const skip = (
    <a className="visually-hidden-focusable cx-skip" href="#main">
      {t('app.skipToContent')}
    </a>
  );

  if (user) {
    return (
      <div className="cx-app">
        {skip}
        <AppNavigation />
        <main id="main" ref={main} className={`cx-app-main${pathname === '/conversations' || pathname === '/private' ? ' is-chat' : ''}`} tabIndex={-1}>
          <Suspense fallback={<p className="cx-muted p-3">{t('auth.working')}</p>}>
            <Page />
          </Suspense>
        </main>
        <NotificationToasts />
        <PrivateChatInvites />
      </div>
    );
  }

  // Signed-out pages (redesign 2026-11): a slim top bar, the page centred on the canvas, and a footer.
  return (
    <div className="cx-shell">
      {skip}
      <header className="cx-topbar">
        <Link to="/" className="cx-brand text-reset text-decoration-none">
          <BrandMark />
          <span className="cx-product">{t('app.name')}</span>
          <span className="cx-badge">{t('app.stage')}</span>
        </Link>
        <nav className="cx-topnav" aria-label={t('nav.label')}>
          <Link to="/sign-in" className="btn btn-sm btn-outline-primary" aria-current={pathname === '/sign-in' ? 'page' : undefined}>
            {t('nav.signIn')}
          </Link>
          <Link to="/register" className="btn btn-sm btn-primary" aria-current={pathname === '/register' ? 'page' : undefined}>
            {t('nav.register')}
          </Link>
        </nav>
      </header>

      <main id="main" ref={main} className="cx-content" tabIndex={-1}>
        <Suspense fallback={<p className="cx-muted p-3">{t('auth.working')}</p>}>
          <Page />
        </Suspense>
      </main>

      <footer className="cx-site-footer">
        <LanguageSelector />
        <p className="cx-footnote mb-0">{t('app.footer')}</p>
      </footer>
    </div>
  );
}

/** Whether the user works a support inbox anywhere (support agent, or organization owner/administrator). */
function useSupportDesk(userId: string | undefined): boolean {
  const { pathname } = useLocation();
  const [desk, setDesk] = useState(false);
  useEffect(() => {
    if (!userId) return;
    let cancelled = false;
    // Re-checked when moving between pages, so a newly granted role shows up without a reload.
    listOrganizations().then((all) => !cancelled && setDesk(supportOrganizations(all).length > 0), () => undefined);
    return () => {
      cancelled = true;
    };
  }, [userId, pathname]);
  return desk;
}

/** The signed-in navigation: a rail on wide screens, a bottom tab bar on phones. */
function AppNavigation() {
  const { t } = useTranslation();
  const { pathname } = useLocation();
  const { user } = useAuth();
  const { unread, invitations, mentions, dueReminders } = useNotifications();
  const activity = mentions + dueReminders;
  const supportDesk = useSupportDesk(user?.id);
  if (!user) return null;

  const items: { to: string; label: string; icon: NavIconName; active: boolean; badge?: number; badgeLabel?: string }[] = [
    { to: '/conversations', label: t('nav.chats'), icon: 'chats', active: pathname === '/conversations', badge: unread, badgeLabel: t('notifications.unread', { count: unread }) },
    { to: '/mentions', label: t('nav.mentions'), icon: 'at', active: pathname === '/mentions', badge: activity, badgeLabel: t('notifications.activity', { count: activity }) },
    { to: '/search', label: t('nav.search'), icon: 'search', active: pathname === '/search' },
    { to: '/channels', label: t('nav.channels'), icon: 'hash', active: pathname === '/channels' || pathname === '/channel' },
    ...(supportDesk ? [{ to: '/support', label: t('nav.support'), icon: 'headset' as const, active: pathname === '/support' }] : []),
    { to: '/private', label: t('nav.private'), icon: 'lock', active: pathname === '/private', badge: invitations, badgeLabel: t('notifications.invitations', { count: invitations }) },
    { to: '/contacts', label: t('nav.contacts'), icon: 'people', active: pathname === '/contacts' },
    { to: '/organizations', label: t('nav.organizations'), icon: 'building', active: pathname === '/organizations' || pathname === '/organization' },
    ...(canAdminister(user.permissions) ? [{ to: '/admin', label: t('nav.admin'), icon: 'shield' as const, active: pathname === '/admin' }] : []),
  ];

  return (
    <nav className="cx-rail" aria-label={t('nav.label')}>
      <Link to="/" className="cx-rail-brand" aria-current={pathname === '/' ? 'page' : undefined} title={t('nav.home')}>
        <BrandMark />
        <span className="visually-hidden">{t('nav.home')}</span>
      </Link>
      <ul className="cx-rail-items list-unstyled">
        {items.map((item) => (
          <li key={item.to}>
            <Link to={item.to} className={`cx-rail-link${item.icon === 'lock' ? ' is-private' : ''}`} aria-current={item.active ? 'page' : undefined}>
              <span className="cx-rail-icon">
                <NavIcon name={item.icon} />
                {item.badge ? (
                  <span className="cx-rail-badge" aria-label={item.badgeLabel}>
                    {item.badge > 99 ? '99+' : item.badge}
                  </span>
                ) : null}
              </span>
              <span className="cx-rail-label">{item.label}</span>
            </Link>
          </li>
        ))}
        <li className="cx-rail-account">
          <Link to="/account" className="cx-rail-link" aria-current={pathname === '/account' ? 'page' : undefined}>
            <span className="cx-rail-icon">
              <Avatar user={user} size="sm" />
            </span>
            <span className="cx-rail-label">{t('nav.profile')}</span>
          </Link>
        </li>
      </ul>
      <div className="cx-rail-status">
        <RealtimeIndicator />
      </div>
    </nav>
  );
}
