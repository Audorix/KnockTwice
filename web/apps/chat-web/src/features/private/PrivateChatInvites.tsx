import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { navigate, useLocation } from '../../app/router';
import type { PrivateChatResponse } from '../../shared/api/contracts';
import { LockGlyph } from '../../shared/components/LockGlyph';
import { useAuth } from '../auth/AuthContext';
import { useRealtime } from '../realtime/RealtimeContext';
import * as api from './privateChatsApi';

/**
 * Incoming private chat invitations, shown on every page: the invitee must see and accept them
 * before anything is said. The invitation itself explains the rules on the next screen.
 */
export function PrivateChatInvites() {
  const { t } = useTranslation();
  const { user } = useAuth();
  const { client } = useRealtime();
  const { pathname, query } = useLocation();
  const [invites, setInvites] = useState<PrivateChatResponse[]>([]);

  const reload = useCallback(() => {
    api.listPrivateChats().then(
      (chats) => setInvites(chats.filter((c) => c.status === 'invited' && !c.isInitiator)),
      () => undefined,
    );
  }, []);

  useEffect(() => {
    if (!client || !user) {
      setInvites([]);
      return;
    }
    reload();
    const off = [client.onPrivateChat(reload), client.onReconnected(reload)];
    return () => off.forEach((o) => o());
  }, [client, user, reload]);

  const visible = invites.filter((invite) => !(pathname === '/private' && query.get('id') === invite.id));
  if (visible.length === 0) return null;

  return (
    <div className="cx-private-invites" role="region" aria-label={t('private.invitesLabel')}>
      {visible.slice(0, 3).map((invite) => (
        <div key={invite.id} className="cx-private-invite" role="alert">
          <span className="cx-private-seal" aria-hidden="true">
            <LockGlyph />
          </span>
          <div className="flex-grow-1 min-w-0">
            <strong className="d-block text-truncate">{t('private.inviteToast', { name: invite.counterpart?.displayName ?? t('conversations.someone') })}</strong>
            <span className="small">{t('private.inviteToastHint')}</span>
          </div>
          <button type="button" className="btn btn-sm cx-private-primary" onClick={() => navigate(`/private?id=${encodeURIComponent(invite.id)}`)}>
            {t('private.view')}
          </button>
        </div>
      ))}
    </div>
  );
}
