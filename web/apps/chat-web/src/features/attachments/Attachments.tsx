import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { AttachmentKind } from '../../shared/api/contracts';
import { errorMessage } from '../auth/errors';

/** Binary units, one decimal: 1.5 KB, 25 MB. */
export function formatBytes(bytes: number, language: string): string {
  const units = ['B', 'KB', 'MB', 'GB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${new Intl.NumberFormat(language, { maximumFractionDigits: unit === 0 ? 0 : 1 }).format(value)} ${units[unit]}`;
}

export interface ShownAttachment {
  id: string;
  fileName: string;
  contentType: string;
  kind: AttachmentKind;
  size: number;
}

/**
 * Files of a message: images as previews (the server serves raster images inline, sandboxed),
 * everything else as a download card. Links open the authorized API URL; nothing is cached publicly.
 */
export function AttachmentList({ items, urlFor }: { items: readonly ShownAttachment[]; urlFor: (item: ShownAttachment, download: boolean) => string }) {
  const { t, i18n } = useTranslation();
  if (items.length === 0) return null;
  return (
    <ul className="cx-attachments list-unstyled" aria-label={t('attachments.listLabel')}>
      {items.map((item) =>
        item.kind === 'image' ? (
          <li key={item.id} className="cx-attachment is-image">
            <a href={urlFor(item, false)} target="_blank" rel="noopener noreferrer" title={item.fileName}>
              <img src={urlFor(item, false)} alt={item.fileName} loading="lazy" decoding="async" />
            </a>
          </li>
        ) : (
          <li key={item.id} className="cx-attachment is-file">
            <FileGlyph />
            <span className="cx-attachment-text">
              <span className="cx-attachment-name">{item.fileName}</span>
              <span className="cx-meta">{formatBytes(item.size, i18n.language)}</span>
            </span>
            <a
              className="btn btn-sm btn-outline-secondary"
              href={urlFor(item, true)}
              download={item.fileName}
              aria-label={`${t('attachments.download')} ${item.fileName}`}
            >
              {t('attachments.download')}
            </a>
          </li>
        ),
      )}
    </ul>
  );
}

function FileGlyph() {
  return (
    <svg className="cx-attachment-glyph" viewBox="0 0 24 24" width="28" height="28" aria-hidden="true" focusable="false">
      <path d="M6 2h8l5 5v13a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2z" fill="none" stroke="currentColor" strokeWidth="1.6" />
      <path d="M14 2v5h5" fill="none" stroke="currentColor" strokeWidth="1.6" />
    </svg>
  );
}

export interface DraftFile {
  key: string;
  file: File;
  status: 'uploading' | 'ready' | 'waiting' | 'failed';
  id?: string;
  error?: unknown;
}

export interface DraftLimits {
  maxFileSize: number;
  maxPerMessage: number;
  allowedExtensions: readonly string[];
}

/**
 * Files chosen for the next message. Each file uploads as soon as it is picked; the message is
 * sent with the ids of the ready ones. Client-side checks only save a round trip: the server
 * validates type, content and size again.
 */
export function useDraftFiles(
  upload: (file: File) => Promise<{ id: string; status?: string }>,
  discard?: (id: string) => Promise<unknown>,
  refresh?: (id: string) => Promise<{ status: string }>,
) {
  const [files, setFiles] = useState<DraftFile[]>([]);
  const filesRef = useRef<DraftFile[]>([]);
  const counter = useRef(0);
  filesRef.current = files;

  const add = useCallback(
    (chosen: readonly File[], limits: DraftLimits | null, reject: (message: string) => void, messages: { tooLarge: string; type: string; tooMany: string }) => {
      const accepted: DraftFile[] = [];
      for (const file of chosen) {
        const extension = file.name.includes('.') ? file.name.split('.').pop()!.toLowerCase() : '';
        if (limits && !limits.allowedExtensions.includes(extension)) {
          reject(`${file.name}: ${messages.type}`);
          continue;
        }
        if (limits && file.size > limits.maxFileSize) {
          reject(`${file.name}: ${messages.tooLarge}`);
          continue;
        }
        accepted.push({ key: `f${++counter.current}`, file, status: 'uploading' });
      }

      const room = limits ? Math.max(0, limits.maxPerMessage - filesRef.current.length) : accepted.length;
      if (accepted.length > room) reject(messages.tooMany);
      const next = accepted.slice(0, room);
      if (next.length === 0) return;
      filesRef.current = [...filesRef.current, ...next];
      setFiles((current) => [...current, ...next]);
      for (const draft of next) {
        upload(draft.file).then(
          (saved) =>
            setFiles((list) =>
              list.map((f) => (f.key === draft.key ? { ...f, id: saved.id, status: saved.status === 'quarantined' ? 'waiting' : 'ready' } : f)),
            ),
          (error: unknown) => setFiles((list) => list.map((f) => (f.key === draft.key ? { ...f, status: 'failed', error } : f))),
        );
      }
    },
    [upload],
  );

  const remove = useCallback(
    (key: string) => {
      const target = filesRef.current.find((f) => f.key === key);
      if (target?.id && discard) void discard(target.id).catch(() => undefined);
      setFiles((current) => current.filter((f) => f.key !== key));
    },
    [discard],
  );

  // Files held back for a malware scan: ask again every few seconds until they are ready.
  const waiting = files.filter((f) => f.status === 'waiting' && f.id).map((f) => f.id!).join(',');
  useEffect(() => {
    if (!waiting || !refresh) return;
    const timer = setInterval(() => {
      for (const id of waiting.split(',')) {
        refresh(id).then(
          (current) => {
            if (current.status === 'ready' || current.status === 'rejected' || current.status === 'deleted') {
              setFiles((list) =>
                list.map((f) => (f.id === id ? { ...f, status: current.status === 'ready' ? 'ready' : 'failed', error: current.status === 'ready' ? undefined : new Error(current.status) } : f)),
              );
            }
          },
          () => undefined,
        );
      }
    }, 5000);
    return () => clearInterval(timer);
  }, [waiting, refresh]);

  /** Forget the drafts after a send (their uploads now belong to the message). */
  const clear = useCallback(() => setFiles([]), []);

  const readyIds = files.filter((f) => f.status === 'ready' && f.id).map((f) => f.id!);
  const busy = files.some((f) => f.status === 'uploading' || f.status === 'waiting');
  return { files, add, remove, clear, readyIds, busy };
}

/** The attach button (a real file input behind a label, so it works with keyboard and screen readers). */
export function AttachButton({ id, onFiles, disabled, accept }: { id: string; onFiles: (files: File[]) => void; disabled?: boolean; accept?: string | undefined }) {
  const { t } = useTranslation();
  return (
    <>
      <input
        id={id}
        type="file"
        className="visually-hidden"
        multiple
        accept={accept}
        disabled={disabled}
        onChange={(e) => {
          const chosen = Array.from(e.target.files ?? []);
          e.target.value = '';
          if (chosen.length > 0) onFiles(chosen);
        }}
      />
      <label htmlFor={id} className={`btn btn-outline-secondary cx-attach-button${disabled ? ' disabled' : ''}`} title={t('attachments.attach')}>
        <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true" focusable="false">
          <path
            d="M21 11.5l-8.6 8.6a5 5 0 0 1-7.1-7.1l8.6-8.6a3.5 3.5 0 0 1 5 5l-8.6 8.6a2 2 0 0 1-2.8-2.8l7.9-7.9"
            fill="none"
            stroke="currentColor"
            strokeWidth="1.8"
            strokeLinecap="round"
          />
        </svg>
        <span className="visually-hidden">{t('attachments.attach')}</span>
      </label>
    </>
  );
}

/** Chips for the files waiting to be sent, with their upload state and a remove button. */
export function DraftFileList({ files, onRemove }: { files: readonly DraftFile[]; onRemove: (key: string) => void }) {
  const { t, i18n } = useTranslation();
  if (files.length === 0) return null;
  return (
    <ul className="cx-draft-files list-unstyled" aria-label={t('attachments.draftLabel')}>
      {files.map((f) => (
        <li key={f.key} className={`cx-draft-file is-${f.status}`}>
          <span className="cx-attachment-name">{f.file.name}</span>
          <span className="cx-meta">
            {f.status === 'uploading'
              ? t('attachments.uploading')
              : f.status === 'waiting'
                ? t('attachments.scanning')
                : f.status === 'failed'
                  ? errorMessage(f.error, t)
                  : formatBytes(f.file.size, i18n.language)}
          </span>
          <button type="button" className="btn-close btn-sm" aria-label={t('attachments.remove', { name: f.file.name })} onClick={() => onRemove(f.key)} />
        </li>
      ))}
    </ul>
  );
}

/** Accept attribute for the file picker from the allowed extensions. */
export const acceptFor = (extensions: readonly string[] | undefined) => (extensions?.length ? extensions.map((e) => `.${e}`).join(',') : undefined);
