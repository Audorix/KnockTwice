import { useCallback, useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { apiUrl } from '../../shared/api/httpClient';
import { FormAlert } from '../../shared/components/Field';
import { NavIcon } from '../../shared/components/NavIcon';
import { useAuth } from '../auth/AuthContext';
import * as api from '../auth/authApi';
import { errorMessage } from '../auth/errors';
import { formatBytes } from '../attachments/Attachments';

const ACCEPTED = ['image/jpeg', 'image/png', 'image/webp'];
const MAX_SOURCE = 5 * 1024 * 1024;
const MAX_UPLOAD = 1024 * 1024;
const OUTPUT = 512;
const MIN_ZOOM = 1;
const MAX_ZOOM = 3;

interface Source {
  url: string;
  name: string | null;
  type: string | null;
  size: number | null;
  revoke: boolean;
}

interface Frame {
  zoom: number;
  rotation: number;
  x: number;
  y: number;
}

const initial: Frame = { zoom: 1.2, rotation: 0, x: 0, y: 0 };

/**
 * Change profile photo (redesign 2026-11, owner mockup): pick an image, drag it into the circle,
 * zoom and rotate, check it at 120, 40 and 24 px, and save. The browser crops and resizes it to a
 * square of 512 px, so only that small image is uploaded; the original never leaves the device.
 */
export function ProfilePhotoDialog({ onClose }: { onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const { user, setUser } = useAuth();
  const [source, setSource] = useState<Source | null>(() =>
    user?.avatar.photoUrl ? { url: apiUrl(user.avatar.photoUrl), name: null, type: null, size: null, revoke: false } : null,
  );
  const [image, setImage] = useState<HTMLImageElement | null>(null);
  const [frame, setFrame] = useState<Frame>(initial);
  const [stage, setStage] = useState(360);
  const [preview, setPreview] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const stageRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const drag = useRef<{ x: number; y: number; frame: Frame } | null>(null);

  // Focus the dialog; Escape closes it.
  useEffect(() => {
    closeRef.current?.focus();
  }, []);

  // The stage follows the dialog's width.
  useLayoutEffect(() => {
    const element = stageRef.current;
    if (!element || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(([entry]) => setStage(Math.round(entry!.contentRect.width)));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  // Load the chosen (or current) image.
  useEffect(() => {
    if (!source) {
      setImage(null);
      return;
    }
    const img = new Image();
    img.decoding = 'async';
    img.onload = () => setImage(img);
    img.onerror = () => {
      setImage(null);
      setNotice(t('profile.photo.unreadable'));
    };
    img.src = source.url;
    return () => {
      if (source.revoke) URL.revokeObjectURL(source.url);
    };
  }, [source, t]);

  const diameter = stage * 0.9;
  const geometry = useCallback(
    (f: Frame) => {
      if (!image) return null;
      const turned = f.rotation % 180 !== 0;
      const width = turned ? image.naturalHeight : image.naturalWidth;
      const height = turned ? image.naturalWidth : image.naturalHeight;
      const scale = (diameter / Math.min(width, height)) * f.zoom;
      return { scale, maxX: Math.max(0, (width * scale - diameter) / 2), maxY: Math.max(0, (height * scale - diameter) / 2) };
    },
    [image, diameter],
  );

  /** Keeps the whole square covered by the image. */
  const clamp = useCallback(
    (f: Frame): Frame => {
      const g = geometry(f);
      if (!g) return f;
      return { ...f, x: Math.max(-g.maxX, Math.min(g.maxX, f.x)), y: Math.max(-g.maxY, Math.min(g.maxY, f.y)) };
    },
    [geometry],
  );

  const update = (change: Partial<Frame>) => setFrame((f) => clamp({ ...f, ...change }));

  /** Draws the square crop at `size` pixels. */
  const render = useCallback(
    (size: number): HTMLCanvasElement | null => {
      const g = geometry(frame);
      if (!image || !g || typeof document === 'undefined') return null;
      const canvas = document.createElement('canvas');
      canvas.width = size;
      canvas.height = size;
      const context = canvas.getContext('2d');
      if (!context) return null;
      const k = size / diameter;
      context.imageSmoothingQuality = 'high';
      context.translate(size / 2 + frame.x * k, size / 2 + frame.y * k);
      context.rotate((frame.rotation * Math.PI) / 180);
      context.scale(g.scale * k, g.scale * k);
      context.drawImage(image, -image.naturalWidth / 2, -image.naturalHeight / 2);
      return canvas;
    },
    [geometry, frame, image, diameter],
  );

  // Previews follow every change (one frame later).
  useEffect(() => {
    if (!image) {
      setPreview(null);
      return;
    }
    const id = requestAnimationFrame(() => {
      try {
        setPreview(render(240)?.toDataURL('image/png') ?? null);
      } catch {
        setPreview(null);
      }
    });
    return () => cancelAnimationFrame(id);
  }, [image, render]);

  const choose = (file: File | undefined) => {
    setNotice(null);
    setError(null);
    if (!file) return;
    if (!ACCEPTED.includes(file.type)) {
      setNotice(t('profile.photo.typeNotAllowed'));
      return;
    }
    if (file.size > MAX_SOURCE) {
      setNotice(t('profile.photo.tooLarge'));
      return;
    }
    setSource({ url: URL.createObjectURL(file), name: file.name, type: file.type, size: file.size, revoke: true });
    setFrame(initial);
  };

  const save = async () => {
    const canvas = render(OUTPUT);
    if (!canvas) return;
    setBusy(true);
    setError(null);
    try {
      const blob = await encode(canvas);
      const extension = blob.type === 'image/webp' ? 'webp' : blob.type === 'image/png' ? 'png' : 'jpg';
      setUser(await api.uploadPhoto(blob, `profile.${extension}`));
      onClose();
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    setBusy(true);
    setError(null);
    try {
      setUser(await api.removePhoto());
      onClose();
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    if (!image) return;
    event.currentTarget.setPointerCapture(event.pointerId);
    drag.current = { x: event.clientX, y: event.clientY, frame };
  };
  const onPointerMove = (event: PointerEvent<HTMLDivElement>) => {
    const start = drag.current;
    if (!start) return;
    setFrame(clamp({ ...start.frame, x: start.frame.x + event.clientX - start.x, y: start.frame.y + event.clientY - start.y }));
  };
  const onStageKey = (event: KeyboardEvent<HTMLDivElement>) => {
    const step = event.shiftKey ? 30 : 10;
    const moves: Record<string, [number, number]> = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] };
    const move = moves[event.key];
    if (!move) return;
    event.preventDefault();
    update({ x: frame.x + move[0], y: frame.y + move[1] });
  };

  const g = geometry(frame);
  const imageStyle = image && g
    ? {
        width: image.naturalWidth,
        height: image.naturalHeight,
        transform: `translate(-50%, -50%) translate(${frame.x}px, ${frame.y}px) rotate(${frame.rotation}deg) scale(${g.scale})`,
      }
    : undefined;

  return (
    <div className="cx-dialog-backdrop" onMouseDown={(e) => { if (e.target === e.currentTarget && !busy) onClose(); }}>
      <div
        className="cx-dialog cx-photo-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="photo-dialog-title"
        aria-describedby="photo-dialog-subtitle"
        onKeyDown={(e) => { if (e.key === 'Escape' && !busy) onClose(); }}
      >
        <header className="cx-dialog-head">
          <span className="cx-dialog-icon" aria-hidden="true"><NavIcon name="camera" size={26} /></span>
          <div className="flex-grow-1">
            <h2 id="photo-dialog-title" className="cx-dialog-title">{t('profile.photo.dialogTitle')}</h2>
            <p id="photo-dialog-subtitle" className="cx-muted mb-0">{t('profile.photo.dialogSubtitle')}</p>
          </div>
          <button ref={closeRef} type="button" className="cx-tool" onClick={onClose} aria-label={t('profile.photo.close')}>
            <NavIcon name="close" size={20} />
          </button>
        </header>

        <div className="cx-photo-body">
          <div className="cx-photo-editor">
            <div
              ref={stageRef}
              className={`cx-photo-stage${image ? ' has-image' : ''}`}
              tabIndex={image ? 0 : -1}
              role={image ? 'img' : undefined}
              aria-label={image ? t('profile.photo.stageLabel') : undefined}
              onPointerDown={onPointerDown}
              onPointerMove={onPointerMove}
              onPointerUp={() => { drag.current = null; }}
              onPointerCancel={() => { drag.current = null; }}
              onKeyDown={onStageKey}
              onDragOver={(e) => e.preventDefault()}
              onDrop={(e) => { e.preventDefault(); choose(e.dataTransfer.files[0]); }}
            >
              {image && imageStyle ? (
                <>
                  <img className="cx-photo-image" src={image.src} alt="" draggable={false} style={imageStyle} />
                  <span className="cx-photo-mask" aria-hidden="true" style={{ width: diameter, height: diameter }}>
                    <span className="cx-photo-grid" />
                    <span className="cx-photo-handle"><NavIcon name="move" size={18} /></span>
                  </span>
                </>
              ) : (
                <div className="cx-photo-empty">
                  <NavIcon name="upload" size={30} />
                  <p className="mb-2">{t('profile.photo.dropHere')}</p>
                  <button type="button" className="btn btn-primary btn-sm" onClick={() => fileRef.current?.click()}>{t('profile.photo.choose')}</button>
                </div>
              )}
            </div>
            {image && <p className="cx-photo-caption">{t('profile.photo.drag')}</p>}
            <div className="cx-photo-zoom">
              <label htmlFor="photo-zoom">{t('profile.photo.zoom')}</label>
              <button type="button" className="cx-tool" onClick={() => update({ zoom: Math.max(MIN_ZOOM, +(frame.zoom - 0.1).toFixed(2)) })} disabled={!image}
                aria-label={t('profile.photo.zoomOut')}>
                <NavIcon name="minus" size={18} />
              </button>
              <input id="photo-zoom" type="range" className="form-range" min={MIN_ZOOM} max={MAX_ZOOM} step={0.01} value={frame.zoom} disabled={!image}
                onChange={(e) => update({ zoom: Number(e.target.value) })} />
              <button type="button" className="cx-tool" onClick={() => update({ zoom: Math.min(MAX_ZOOM, +(frame.zoom + 0.1).toFixed(2)) })} disabled={!image}
                aria-label={t('profile.photo.zoomIn')}>
                <NavIcon name="plus" size={18} />
              </button>
            </div>
            <div className="cx-photo-tools">
              <button type="button" className="btn btn-outline-secondary btn-sm cx-icon-button" disabled={!image}
                onClick={() => update({ rotation: (frame.rotation + 270) % 360 })}>
                <NavIcon name="rotate-left" size={16} />
                {t('profile.photo.rotateLeft')}
              </button>
              <button type="button" className="btn btn-outline-secondary btn-sm cx-icon-button" disabled={!image}
                onClick={() => update({ rotation: (frame.rotation + 90) % 360 })}>
                <NavIcon name="rotate-right" size={16} />
                {t('profile.photo.rotateRight')}
              </button>
              <button type="button" className="btn btn-link btn-sm" disabled={!image} onClick={() => setFrame(initial)}>{t('profile.photo.reset')}</button>
            </div>
          </div>

          <div className="cx-photo-side">
            <h3 className="cx-photo-side-title">{t('profile.photo.preview')}</h3>
            <p className="cx-muted">{t('profile.photo.previewHint')}</p>
            <div className="cx-photo-previews">
              {[120, 40, 24].map((size) => (
                <figure key={size} className="cx-photo-preview">
                  <span className="cx-photo-preview-circle" style={{ width: size, height: size }}>
                    {preview ? <img src={preview} alt="" /> : <span className="cx-photo-preview-initials">{user?.avatar.initials}</span>}
                  </span>
                  <figcaption>{t('profile.photo.px', { size })}</figcaption>
                </figure>
              ))}
            </div>
            <div className="cx-photo-file">
              <NavIcon name="file" size={26} />
              <div className="min-w-0">
                <strong className="d-block text-truncate">{source?.name ?? (source ? t('profile.photo.current') : t('profile.photo.none'))}</strong>
                {source?.size != null && (
                  <span className="cx-muted small">{(source.type ?? '').replace('image/', '').toUpperCase()} · {formatBytes(source.size, i18n.language)}</span>
                )}
              </div>
            </div>
            <input ref={fileRef} id="photo-file" type="file" className="visually-hidden" accept={ACCEPTED.join(',')}
              onChange={(e) => { choose(e.target.files?.[0]); e.target.value = ''; }} />
            <button type="button" className="btn btn-outline-primary w-100 cx-icon-button justify-content-center" onClick={() => fileRef.current?.click()}>
              <NavIcon name="upload" size={18} />
              {source ? t('profile.photo.chooseAnother') : t('profile.photo.choose')}
            </button>
            {notice && <p className="text-danger small mt-2 mb-0" role="alert">{notice}</p>}
          </div>
        </div>
        <p className="cx-photo-limits">{t('profile.photo.limits')}</p>
        {error !== null && <FormAlert tone="danger">{errorMessage(error, t)}</FormAlert>}

        <footer className="cx-dialog-foot">
          {user?.avatar.photoUrl ? (
            <button type="button" className="cx-photo-remove" onClick={() => void remove()} disabled={busy}>
              <NavIcon name="trash" size={22} />
              <span>
                <strong className="d-block">{t('profile.photo.remove')}</strong>
                <span className="cx-muted small">{t('profile.photo.removeHint')}</span>
              </span>
            </button>
          ) : (
            <span />
          )}
          <div className="d-flex gap-2">
            <button type="button" className="btn btn-outline-secondary" onClick={onClose} disabled={busy}>{t('profile.photo.cancel')}</button>
            <button type="button" className="btn btn-primary" onClick={() => void save()} disabled={!image || busy}>{t('profile.photo.save')}</button>
          </div>
        </footer>
      </div>
    </div>
  );
}

/** WebP where the browser can make it, else PNG; JPEG when that is still over the upload limit. */
async function encode(canvas: HTMLCanvasElement): Promise<Blob> {
  const toBlob = (type: string, quality?: number) =>
    new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, type, quality));
  let blob = await toBlob('image/webp', 0.9);
  if (!blob || blob.type !== 'image/webp') blob = await toBlob('image/png');
  if (!blob || blob.size > MAX_UPLOAD) blob = await toBlob('image/jpeg', 0.85);
  if (!blob) throw new Error('encode');
  return blob;
}
