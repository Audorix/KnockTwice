import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, navigate, useLocation } from '../../app/router';
import type { ContactDetailsResponse, ContactResponse } from '../../shared/api/contracts';
import { Avatar } from '../../shared/components/Avatar';
import { FormAlert } from '../../shared/components/Field';
import { NavIcon } from '../../shared/components/NavIcon';
import { useConfirm } from '../../shared/components/ConfirmDialog';
import { getUserLinks, type ProfileLink } from '../auth/authApi';
import { errorMessage } from '../auth/errors';
import { RequireUser } from '../auth/RequireUser';
import { LinkItem } from '../account/ProfileLinks';
import { openDirect } from '../conversations/conversationsApi';
import { useRealtime } from '../realtime/RealtimeContext';
import type { PresenceValue } from '../realtime/realtimeClient';
import * as api from './contactsApi';

type Presence = PresenceValue;

function formatDay(value: string, language: string) {
  return new Intl.DateTimeFormat(language, { dateStyle: 'medium' }).format(new Date(value));
}

function useContactDetails(userId: string) {
  const [details, setDetails] = useState<ContactDetailsResponse | null>(null);
  const [links, setLinks] = useState<ProfileLink[]>([]);
  useEffect(() => {
    let cancelled = false;
    api.getContactDetails(userId).then((d) => !cancelled && setDetails(d), () => !cancelled && setDetails({ handle: null, email: null, emailShared: false }));
    getUserLinks(userId).then((l) => !cancelled && setLinks(l), () => !cancelled && setLinks([]));
    return () => {
      cancelled = true;
    };
  }, [userId]);
  return { details, links };
}

/** Copies `value` with one click and says so for two seconds (announced to screen readers). */
function CopyButton({ value, label }: { value: string; label: string }) {
  const { t } = useTranslation();
  const [copied, setCopied] = useState(false);
  useEffect(() => {
    if (!copied) return;
    const id = window.setTimeout(() => setCopied(false), 2000);
    return () => window.clearTimeout(id);
  }, [copied]);
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
    } catch {
      setCopied(false);
    }
  };
  return (
    <button type="button" className={`cx-tool cx-copy${copied ? ' is-copied' : ''}`} onClick={() => void copy()} title={copied ? t('contactProfile.copied') : label}>
      <NavIcon name={copied ? 'check' : 'copy'} size={20} />
      <span className="visually-hidden">{label}</span>
      <span className="visually-hidden" role="status">{copied ? t('contactProfile.copied') : ''}</span>
    </button>
  );
}

function PresenceText({ value }: { value: Presence }) {
  const { t } = useTranslation();
  return (
    <span className="cx-contact-presence">
      <span className={`cx-dot ${value === 'online' ? 'cx-dot-ok' : 'cx-dot-idle'}`} aria-hidden="true" />
      {t(`contacts.presence.${value}`)}
    </span>
  );
}

/** Email (or "Email not shared") and Display ID, each with a copy button. */
function DetailsCard({ details }: { details: ContactDetailsResponse | null }) {
  const { t } = useTranslation();
  return (
    <section className="cx-contact-card" aria-labelledby="contact-details-title">
      <h3 id="contact-details-title" className="cx-contact-card-title">{t('contactProfile.details')}</h3>
      <dl className="cx-contact-details mb-0">
        <div>
          <dt>{t('contactProfile.email')}</dt>
          <dd>
            {details === null ? (
              <span className="cx-muted">{t('auth.working')}</span>
            ) : details.email ? (
              <>
                <span className="text-truncate">{details.email}</span>
                <CopyButton value={details.email} label={t('contactProfile.copyEmail')} />
              </>
            ) : (
              <span className="cx-muted">{t('contactProfile.emailNotShared')}</span>
            )}
          </dd>
        </div>
        <div>
          <dt>{t('contactProfile.displayId')}</dt>
          <dd>
            {details?.handle ? (
              <>
                <span className="text-truncate">@{details.handle}</span>
                <CopyButton value={`@${details.handle}`} label={t('contactProfile.copyDisplayId')} />
              </>
            ) : (
              <span className="cx-muted">{details === null ? t('auth.working') : '—'}</span>
            )}
          </dd>
        </div>
      </dl>
    </section>
  );
}

function SharedOrganizations({ contact }: { contact: ContactResponse }) {
  const { t } = useTranslation();
  const organizations = contact.sharedOrganizations ?? [];
  return (
    <section className="cx-contact-card cx-contact-orgs" aria-labelledby="contact-orgs-title">
      <NavIcon name="building" size={24} />
      <div className="min-w-0">
        <h3 id="contact-orgs-title" className="cx-contact-card-title mb-1">{t('contacts.profileOrganizations')}</h3>
        {organizations.length > 0 ? (
          <span className="cx-tags">{organizations.map((o) => <span key={o.id} className="cx-tag">{o.name}</span>)}</span>
        ) : (
          <span className="cx-muted">{t('contacts.profileNoOrganizations')}</span>
        )}
      </div>
    </section>
  );
}

function ContactSince({ contact }: { contact: ContactResponse }) {
  const { t, i18n } = useTranslation();
  return (
    <p className="cx-contact-since">
      <NavIcon name="calendar" size={22} />
      {t('contacts.profileSince', { date: formatDay(contact.sinceUtc, i18n.language) })}
    </p>
  );
}

/** "Manage contact": Remove contact and Block contact, out of the way of everyday actions. */
function ManageMenu({ onRemove, onBlock }: { onRemove: () => void; onBlock: () => void }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const box = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (!open) return;
    const close = (event: MouseEvent) => {
      if (box.current && !box.current.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', close);
    box.current?.querySelector<HTMLButtonElement>('[role="menuitem"]')?.focus();
    return () => document.removeEventListener('mousedown', close);
  }, [open]);
  // Focus back on the trigger before acting, so a dialog the action opens returns focus there.
  const choose = (action: () => void) => {
    setOpen(false);
    trigger.current?.focus();
    action();
  };
  return (
    <div className="cx-contact-manage" ref={box} onKeyDown={(e) => {
      if (e.key === 'Escape' && open) {
        e.stopPropagation();
        setOpen(false);
        trigger.current?.focus();
      } else if (open && (e.key === 'ArrowDown' || e.key === 'ArrowUp')) {
        e.preventDefault();
        const items = Array.from(box.current?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]') ?? []);
        const index = items.indexOf(document.activeElement as HTMLButtonElement);
        items[(index + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length]?.focus();
      }
    }}>
      <button ref={trigger} type="button" className="cx-contact-manage-button" aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen((o) => !o)}>
        <NavIcon name="dots" size={22} />
        {t('contactProfile.manage')}
      </button>
      {open && (
        <div className="cx-menu cx-contact-manage-menu" role="menu">
          <button type="button" role="menuitem" className="cx-menu-item" onClick={() => choose(onRemove)}>
            <NavIcon name="user" size={18} />
            {t('contacts.removeContact')}
          </button>
          <button type="button" role="menuitem" className="cx-menu-item is-danger" onClick={() => choose(onBlock)}>
            <NavIcon name="block" size={18} />
            {t('contacts.blockContact')}
          </button>
        </div>
      )}
    </div>
  );
}

/**
 * Contact profile drawer (owner mockup 2026-11-24): photo and status, Message and View full profile
 * side by side, contact details with one-click copy, shared organizations, the contact date, and
 * Remove/Block under "Manage contact". A modal dialog on the right; Escape, × or the backdrop close it.
 */
export function ContactDrawer({ contact, status, onClose, onMessage, onRemove, onBlock }: {
  contact: ContactResponse;
  status: Presence;
  onClose: () => void;
  onMessage: () => void;
  onRemove: () => void;
  onBlock: () => void;
}) {
  const { t } = useTranslation();
  const closeButton = useRef<HTMLButtonElement>(null);
  const opener = useRef<Element | null>(null);
  const { details } = useContactDetails(contact.user.id);

  useEffect(() => {
    opener.current = document.activeElement;
    closeButton.current?.focus();
    return () => (opener.current as HTMLElement | null)?.focus?.();
  }, []);

  return (
    <div className="cx-drawer-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <aside className="cx-drawer cx-contact-drawer" role="dialog" aria-modal="true" aria-labelledby="contact-drawer-title"
        onKeyDown={(e) => e.key === 'Escape' && onClose()}>
        <header className="cx-contact-drawer-head">
          <h2 id="contact-drawer-title" className="cx-contact-drawer-title">{t('contactProfile.title')}</h2>
          <button ref={closeButton} type="button" className="cx-tool" onClick={onClose} title={t('contacts.closeProfile')}>
            <NavIcon name="close" size={20} />
            <span className="visually-hidden">{t('contacts.closeProfile')}</span>
          </button>
        </header>

        <div className="cx-contact-drawer-body">
          <div className="cx-contact-identity">
            <Avatar user={contact.user} size="xl" />
            <div className="min-w-0">
              <p className="cx-contact-name">{contact.user.displayName}</p>
              <PresenceText value={status} />
            </div>
          </div>

          <div className="cx-contact-actions">
            <button type="button" className="btn btn-primary cx-icon-button" onClick={onMessage}>
              <NavIcon name="message" size={20} />
              {t('contacts.message')}
            </button>
            <Link to={`/person?id=${encodeURIComponent(contact.user.id)}`} className="btn btn-outline-secondary cx-icon-button">
              <NavIcon name="arrow-right" size={20} />
              {t('contactProfile.viewFull')}
            </Link>
          </div>

          <DetailsCard details={details} />
          <SharedOrganizations contact={contact} />
          <ContactSince contact={contact} />
        </div>

        <footer className="cx-contact-drawer-foot">
          <ManageMenu onRemove={onRemove} onBlock={onBlock} />
        </footer>
      </aside>
    </div>
  );
}

/** A contact's full profile (`/person?id=`): everything in the drawer, plus their profile links. */
export function PersonPage() {
  return (
    <RequireUser>
      <Person />
    </RequireUser>
  );
}

function Person() {
  const { t } = useTranslation();
  const { query } = useLocation();
  const userId = query.get('id') ?? '';
  const { presence } = useRealtime();
  const [contact, setContact] = useState<ContactResponse | null | undefined>(undefined);
  const [error, setError] = useState<unknown>(null);
  const { details, links } = useContactDetails(userId);
  const [confirm, confirmDialog] = useConfirm();

  useEffect(() => {
    api.listContacts().then((all) => setContact(all.find((c) => c.user.id === userId) ?? null), () => setContact(null));
  }, [userId]);

  if (contact === undefined) return <p className="cx-muted">{t('auth.working')}</p>;
  if (contact === null) {
    return (
      <>
        <FormAlert tone="danger">{t('errors.contact_not_found')}</FormAlert>
        <Link to="/contacts">{t('contactProfile.backToContacts')}</Link>
      </>
    );
  }

  const status: Presence = presence.get(contact.user.id) ?? 'offline';
  const message = () =>
    void openDirect(contact.user.id).then((c) => navigate(`/conversations?id=${encodeURIComponent(c.id)}`), setError);
  const remove = () =>
    void confirm({
      title: t('contactProfile.removeTitle', { name: contact.user.displayName }),
      text: t('contactProfile.removeText'),
      confirmLabel: t('contacts.removeContact'),
    }).then((ok) => { if (ok) void api.removeContact(contact.user.id).then(() => navigate('/contacts'), setError); });
  const block = () =>
    void confirm({
      title: t('confirm.block.title', { name: contact.user.displayName }),
      text: t('confirm.block.text'),
      confirmLabel: t('confirm.block.action'),
    }).then((ok) => { if (ok) void api.blockUser(contact.user.id).then(() => navigate('/contacts?tab=blocked'), setError); });

  return (
    <div className="cx-person-page">
      {confirmDialog}
      <nav aria-label={t('nav.label')} className="cx-breadcrumb">
        <Link to="/contacts">{t('contacts.title')}</Link>
        <span aria-hidden="true">/</span>
        <span aria-current="page">{contact.user.displayName}</span>
      </nav>
      {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}
      <section className="cx-card cx-person-hero" aria-labelledby="person-title">
        <Avatar user={contact.user} size="xl" />
        <div className="min-w-0 flex-grow-1">
          <h1 id="person-title" className="cx-person-page-name text-truncate">{contact.user.displayName}</h1>
          {details?.handle && <p className="cx-muted mb-1">@{details.handle}</p>}
          <PresenceText value={status} />
        </div>
        <div className="cx-contact-actions">
          <button type="button" className="btn btn-primary cx-icon-button" onClick={message}>
            <NavIcon name="message" size={20} />
            {t('contacts.message')}
          </button>
          <ManageMenu onRemove={remove} onBlock={block} />
        </div>
      </section>
      <div className="cx-person-grid">
        <div className="d-flex flex-column gap-3">
          <DetailsCard details={details} />
          <SharedOrganizations contact={contact} />
          <ContactSince contact={contact} />
        </div>
        <section className="cx-contact-card" aria-labelledby="person-links-title">
          <h2 id="person-links-title" className="cx-contact-card-title">{t('contacts.profileLinks')}</h2>
          {links.length > 0 ? (
            <ul className="list-unstyled cx-link-list mb-0">
              {links.map((link) => <LinkItem key={`${link.platform}-${link.url}`} link={link} />)}
            </ul>
          ) : (
            <p className="cx-muted mb-0">{t('contactProfile.noLinks')}</p>
          )}
        </section>
      </div>
    </div>
  );
}
