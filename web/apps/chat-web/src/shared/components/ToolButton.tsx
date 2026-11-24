import type { ButtonHTMLAttributes } from 'react';
import { NavIcon, type NavIconName } from './NavIcon';

/**
 * A small icon button (message tools, composer). The label is the accessible name and the
 * tooltip, so screen readers and tests find it by its words, not its picture.
 */
export function ToolButton({
  icon,
  label,
  danger = false,
  className,
  ...rest
}: { icon: NavIconName; label: string; danger?: boolean } & Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'children' | 'type'>) {
  return (
    <button type="button" className={`cx-tool${danger ? ' is-danger' : ''}${className ? ` ${className}` : ''}`} title={label} {...rest}>
      <NavIcon name={icon} size={17} />
      <span className="visually-hidden">{label}</span>
    </button>
  );
}
