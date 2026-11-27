import { useCallback, useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { trapTab } from '../../shared/components/ConfirmDialog';
import { FormAlert } from '../../shared/components/Field';
import { NavIcon } from '../../shared/components/NavIcon';
import { errorMessage } from '../auth/errors';
import { formatBytes } from '../attachments/Attachments';

const ACCEPTED = ['image/jpeg', 'image/png', 'image/webp'];
const MAX_SOURCE = 5 * 1024 * 1024;
const MAX_UPLOAD = 1024 * 1024;
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

export interface ImageCropOptions {
  title: string;
  subtitle: string;
  /** circle (profile photos), rounded (organization pictures) or rect (background images). */
  shape: 'circle' | 'rounded' | 'rect';
  /** Width and height of the uploaded image in pixels; their ratio is the crop frame's. */
  output: { width: number; height: number };
  /** Preview widths in pixels (heights follow the ratio). */
  previews: number[];
  /** The current image's address, edited when no other file is chosen. */
  currentUrl: string | null;
  /** Shown in the previews when there is no image. */
  placeholder: string;
  upload: (blob: Blob, fileName: string) => Promise<void>;
  remove?: (() => Promise<void>) | undefined;
  removeLabel?: string;
  removeHint?: string;
  /** Defaults to the profile photo wording. */
  saveLabel?: string;
  currentLabel?: string;
  modeSwitcher?: ReactNode;
}

/**
 * Pick an image, drag it into the frame, zoom and rotate, check the previews, and save (redesign
 * 2026-11, owner mockups). The browser crops and resizes it, so only the small result is uploaded;
 * the original never leaves the device. Used for profile photos, organization pictures and
 * organization background images.
 */
export function ImageCropDialog({ options, onClose }: { options: ImageCropOptions; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const { output, shape } = options;
  const aspect = output.width / output.height;
  const [source, setSource] = useState<Source | null>(() =>
    options.currentUrl ? { url: options.currentUrl, name: null, type: null, size: null, revoke: false } : null,
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

  // The crop frame: 90% of the stage's width, its height from the ratio (square stage for squares).
  const frameWidth = stage * 0.9;
  const frameHeight = frameWidth / aspect;
  const stageHeight = aspect > 1 ? frameHeight + stage * 0.1 : stage;

  const geometry = useCallback(
    (f: Frame) => {
      if (!image) return null;
      const turned = f.rotation % 180 !== 0;
      const width = turned ? image.naturalHeight : image.naturalWidth;
      const height = turned ? image.naturalWidth : image.naturalHeight;
      const scale = Math.max(frameWidth / width, frameHeight / height) * f.zoom;
      return { scale, maxX: Math.max(0, (width * scale - frameWidth) / 2), maxY: Math.max(0, (height * scale - frameHeight) / 2) };
    },
    [image, frameWidth, frameHeight],
  );

  /** Keeps the whole frame covered by the image. */
  const clamp = useCallback(
    (f: Frame): Frame => {
      const g = geometry(f);
      if (!g) return f;
      return { ...f, x: Math.max(-g.maxX, Math.min(g.maxX, f.x)), y: Math.max(-g.maxY, Math.min(g.maxY, f.y)) };
    },
    [geometry],
  );

  const update = (change: Partial<Frame>) => setFrame((f) => clamp({ ...f, ...change }));

  // The stage can change size (window resized, phone turned): keep the frame covered.
  useEffect(() => {
    setFrame((f) => {
      const next = clamp(f);
      return next.x === f.x && next.y === f.y ? f : next;
    });
  }, [clamp]);

  /** Draws the crop at `width` pixels wide. */
  const render = useCallback(
    (width: number): HTMLCanvasElement | null => {
      const g = geometry(frame);
      if (!image || !g || typeof document === 'undefined') return null;
      const height = Math.round(width / aspect);
      const canvas = document.createElement('canvas');
      canvas.width = width;
      canvas.height = height;
      const context = canvas.getContext('2d');
      if (!context) return null;
      const k = width / frameWidth;
      context.imageSmoothingQuality = 'high';
      context.translate(width / 2 + frame.x * k, height / 2 + frame.y * k);
      context.rotate((frame.rotation * Math.PI) / 180);
      context.scale(g.scale * k, g.scale * k);
      context.drawImage(image, -image.naturalWidth / 2, -image.naturalHeight / 2);
      return canvas;
    },
    [geometry, frame, image, frameWidth, aspect],
  );

  // Previews follow every change (one frame later).
  useEffect(() => {
    if (!image) {
      setPreview(null);
      return;
    }
    const id = requestAnimationFrame(() => {
      try {
        setPreview(render(aspect > 1 ? 480 : 240)?.toDataURL('image/png') ?? null);
      } catch {
        setPreview(null);
      }
    });
    return () => cancelAnimationFrame(id);
  }, [image, render, aspect]);

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

  const run = async (action: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    try {
      await action();
      onClose();
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const save = () =>
    run(async () => {
      const canvas = render(output.width);
      if (!canvas) return;
      const blob = await encode(canvas);
      const extension = blob.type === 'image/webp' ? 'webp' : blob.type === 'image/png' ? 'png' : 'jpg';
      await options.upload(blob, `image.${extension}`);
    });

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
        className={`cx-dialog cx-photo-dialog${aspect > 1 ? ' is-wide' : ''}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby="photo-dialog-title"
        aria-describedby="photo-dialog-subtitle"
        onKeyDown={(e) => {
          if (e.key === 'Escape') {
            e.stopPropagation();
            if (!busy) onClose();
          } else {
            trapTab(e);
          }
        }}
      >
        <header className="cx-dialog-head">
          <span className="cx-dialog-icon" aria-hidden="true"><NavIcon name="camera" size={26} /></span>
          <div className="flex-grow-1">
            <h2 id="photo-dialog-title" className="cx-dialog-title">{options.title}</h2>
            <p id="photo-dialog-subtitle" className="cx-muted mb-0">{options.subtitle}</p>
          </div>
          <button ref={closeRef} type="button" className="cx-tool" onClick={onClose} aria-label={t('profile.photo.close')}>
            <NavIcon name="close" size={20} />
          </button>
        </header>
        {options.modeSwitcher}

        <div className="cx-photo-body">
          <div className="cx-photo-editor">
            <div
              ref={stageRef}
              className={`cx-photo-stage${image ? ' has-image' : ''}`}
              style={aspect > 1 ? { aspectRatio: 'auto', height: stageHeight } : undefined}
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
                  <span className={`cx-photo-mask is-${shape}`} aria-hidden="true" style={{ width: frameWidth, height: frameHeight }}>
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
              {options.previews.map((size) => (
                <figure key={size} className="cx-photo-preview">
                  <span className={`cx-photo-preview-circle is-${shape}`} style={{ width: size, height: Math.round(size / aspect) }}>
                    {preview ? <img src={preview} alt="" /> : <span className="cx-photo-preview-initials">{options.placeholder}</span>}
                  </span>
                  <figcaption>{t('profile.photo.px', { size })}</figcaption>
                </figure>
              ))}
            </div>
            <div className="cx-photo-file">
              <NavIcon name="file" size={26} />
              <div className="min-w-0">
                <strong className="d-block text-truncate">{source?.name ?? (source ? (options.currentLabel ?? t('profile.photo.current')) : t('profile.photo.none'))}</strong>
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
          {options.remove ? (
            <button type="button" className="cx-photo-remove" onClick={() => void run(options.remove!)} disabled={busy}>
              <NavIcon name="trash" size={22} />
              <span>
                <strong className="d-block">{options.removeLabel}</strong>
                <span className="cx-muted small">{options.removeHint}</span>
              </span>
            </button>
          ) : (
            <span />
          )}
          <div className="d-flex gap-2">
            <button type="button" className="btn btn-outline-secondary" onClick={onClose} disabled={busy}>{t('profile.photo.cancel')}</button>
            <button type="button" className="btn btn-primary" onClick={() => void save()} disabled={!image || busy}>{options.saveLabel ?? t('profile.photo.save')}</button>
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
