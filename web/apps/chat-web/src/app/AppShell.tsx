import { Fragment, lazy, Suspense, useEffect, useRef, useState, type ComponentType } from 'react';
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
import { currentLanguage, selectLanguage, supportedLanguages } from '../i18n/i18n';
import { chooseLocalTheme, currentResolvedTheme } from '../shared/theme';

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
  const { pathname, query } = useLocation();
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
        {/* Full-height chat layout for conversations and an open private chat (not the private chats list or invitation). */}
        <main id="main" ref={main} className={`cx-app-main${pathname === '/conversations' || (pathname === '/private' && query.get('id')) ? ' is-chat' : ''}`} tabIndex={-1}>
          <Suspense fallback={<p className="cx-muted p-3">{t('auth.working')}</p>}>
            <Page />
          </Suspense>
        </main>
        <NotificationToasts />
        <PrivateChatInvites />
      </div>
    );
  }

  // Signed-out pages (redesign 2026-11, owner mockups): the brand, then Features or "Back to home",
  // a light/dark switch, the language and the account buttons; the page; a footer.
  const home = pathname === '/';
  return (
    <div className={`cx-shell${home ? ' is-landing' : ' is-auth'}`}>
      {skip}
      <header className="cx-topbar">
        <Link to="/" className="cx-brand text-reset text-decoration-none">
          <BrandMark />
          <span className="cx-brand-text">
            <span className="cx-product">{t('app.name')}</span>
            <span className="cx-brand-by">{t('nav.brandBy')}</span>
          </span>
        </Link>
        <nav className="cx-topnav" aria-label={t('nav.label')}>
          {home ? (
            <a href="#features" className="cx-topnav-link">{t('landing.featuresLink')}</a>
          ) : (
            <Link to="/" className="cx-topnav-link">
              <NavIcon name="back" size={18} />
              {t('landing.backHome')}
            </Link>
          )}
          <ThemeSwitch />
          <LanguageSelector compact />
          {home && (
            <>
              <Link to="/sign-in" className="btn btn-outline-primary">{t('nav.signIn')}</Link>
              <Link to="/register" className="btn btn-primary">{t('nav.register')}</Link>
            </>
          )}
        </nav>
      </header>

      <main id="main" ref={main} className="cx-content" tabIndex={-1}>
        <Suspense fallback={<p className="cx-muted p-3">{t('auth.working')}</p>}>
          <Page />
        </Suspense>
      </main>

      <footer className="cx-site-footer">
        <span className="cx-footer-brand">
          <BrandMark size={30} />
          <strong>{t('landing.footerBrand')}</strong>
        </span>
        <span className="cx-footer-divider" aria-hidden="true" />
        <p className="cx-footnote mb-0">{t('landing.footerTagline')}</p>
        <span className="cx-footer-spacer" />
        <span className="cx-footer-languages" role="group" aria-label={t('language.label')}>
          {supportedLanguages.map((language, index) => (
            <Fragment key={language}>
              {index > 0 && <span aria-hidden="true">/</span>}
              <button type="button" className="btn btn-link p-0" lang={language} aria-pressed={currentLanguage() === language}
                onClick={() => void selectLanguage(language)}>
                {t(`language.${language}`)}
              </button>
            </Fragment>
          ))}
        </span>
        <span className="cx-footer-divider" aria-hidden="true" />
        <span className="cx-footnote">{t('landing.licenseShort')}</span>
      </footer>
    </div>
  );
}

/** Light/dark for signed-out visitors, remembered in this browser (redesign 2026-11). */
function ThemeSwitch() {
  const { t } = useTranslation();
  const [theme, setTheme] = useState(currentResolvedTheme);
  const choose = (value: 'light' | 'dark') => {
    chooseLocalTheme(value);
    setTheme(value);
  };
  return (
    <span className="cx-theme-switch" role="group" aria-label={t('landing.theme')}>
      {(['light', 'dark'] as const).map((value) => (
        <button key={value} type="button" className={theme === value ? 'is-active' : undefined} aria-pressed={theme === value}
          onClick={() => choose(value)} title={t(`preferences.themes.${value}`)}>
          <NavIcon name={value === 'light' ? 'sun' : 'moon'} size={18} />
          <span className="visually-hidden">{t(`preferences.themes.${value}`)}</span>
        </button>
      ))}
    </span>
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

  type Item = { to: string; label: string; icon: NavIconName; active: boolean; badge?: number; badgeLabel?: string };
  const communicate: Item[] = [
    { to: '/', label: t('nav.homeItem'), icon: 'home', active: pathname === '/' },
    { to: '/conversations', label: t('nav.chats'), icon: 'chats', active: pathname === '/conversations', badge: unread, badgeLabel: t('notifications.unread', { count: unread }) },
    { to: '/mentions', label: t('nav.mentions'), icon: 'at', active: pathname === '/mentions', badge: activity, badgeLabel: t('notifications.activity', { count: activity }) },
    { to: '/search', label: t('nav.search'), icon: 'search', active: pathname === '/search' },
    { to: '/channels', label: t('nav.channels'), icon: 'hash', active: pathname === '/channels' || pathname === '/channel' },
    ...(supportDesk ? [{ to: '/support', label: t('nav.support'), icon: 'headset' as const, active: pathname === '/support' }] : []),
    { to: '/private', label: t('nav.private'), icon: 'lock', active: pathname === '/private', badge: invitations, badgeLabel: t('notifications.invitations', { count: invitations }) },
  ];
  const workspace: Item[] = [
    { to: '/contacts', label: t('nav.contacts'), icon: 'people', active: pathname === '/contacts' },
    { to: '/organizations', label: t('nav.organizations'), icon: 'building', active: pathname === '/organizations' || pathname === '/organization' },
    ...(canAdminister(user.permissions) ? [{ to: '/admin', label: t('nav.admin'), icon: 'shield' as const, active: pathname === '/admin' }] : []),
  ];

  const renderItem = (item: Item) => (
    // Home stays out of the phone tab bar (the brand mark leads home there).
    <li key={item.to} className={item.icon === 'home' ? 'cx-rail-home' : undefined}>
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
  );

  // Redesign 2026-11: a labelled sidebar on wide screens (brand, the communication pages, a
  // Workspace section and the signed-in person at the bottom), an icon rail in between, and a
  // bottom tab bar on phones — all from this one markup.
  return (
    <nav className="cx-rail" aria-label={t('nav.label')}>
      <Link to="/" className="cx-rail-brand" aria-current={pathname === '/' ? 'page' : undefined} title={t('nav.home')}>
        <BrandMark />
        <span className="cx-rail-brand-text" aria-hidden="true">
          <span className="cx-rail-brand-name">{t('nav.brandName')}</span>
          <span className="cx-rail-brand-by">{t('nav.brandBy')}</span>
        </span>
        <span className="visually-hidden">{t('nav.home')}</span>
      </Link>
      <ul className="cx-rail-items list-unstyled">{communicate.map(renderItem)}</ul>
      <p className="cx-rail-section" aria-hidden="true">{t('nav.workspace')}</p>
      <ul className="cx-rail-items list-unstyled" aria-label={t('nav.workspace')}>{workspace.map(renderItem)}</ul>
      <Link to="/account" className="cx-rail-user" aria-current={pathname === '/account' ? 'page' : undefined}>
        <Avatar user={user} size="sm" />
        <span className="cx-rail-user-text">
          <span className="cx-rail-user-name">{user.displayName}</span>
          <RealtimeIndicator />
        </span>
        <span className="cx-rail-user-settings">
          <NavIcon name="settings" size={20} />
        </span>
        <span className="cx-rail-label cx-rail-user-label">{t('nav.profile')}</span>
      </Link>
    </nav>
  );
}
