import { useId, type InputHTMLAttributes } from 'react';

type FieldProps = InputHTMLAttributes<HTMLInputElement> & {
  label: string;
  errors?: readonly string[] | undefined;
  hint?: string | undefined;
};

/** Labelled Bootstrap input with accessible error and hint text. */
export function Field({ label, errors = [], hint, className, ...input }: FieldProps) {
  const id = useId();
  const errorId = `${id}-error`;
  const hintId = `${id}-hint`;
  const describedBy = [errors.length ? errorId : null, hint ? hintId : null].filter(Boolean).join(' ') || undefined;

  return (
    <div className={`mb-3 ${className ?? ''}`}>
      <label htmlFor={id} className="form-label">
        {label}
      </label>
      <input
        id={id}
        className={`form-control${errors.length ? ' is-invalid' : ''}`}
        aria-invalid={errors.length > 0 || undefined}
        aria-describedby={describedBy}
        {...input}
      />
      {hint && (
        <div id={hintId} className="form-text">
          {hint}
        </div>
      )}
      {errors.length > 0 && (
        <div id={errorId} className="invalid-feedback d-block">
          {errors.join(' ')}
        </div>
      )}
    </div>
  );
}

/** Polite status or assertive error message for form outcomes. */
export function FormAlert({ tone, children }: { tone: 'danger' | 'success' | 'info'; children: string }) {
  return (
    <div className={`alert alert-${tone} py-2`} role={tone === 'danger' ? 'alert' : 'status'}>
      {children}
    </div>
  );
}
