import { apiUrl } from '../../shared/api/httpClient';

export const organizationInitials = (name: string) =>
  name.split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0]!.toUpperCase()).join('') || '?';

/** The organization's picture, or its initials on a tinted square. Decorative: the name is shown beside it. */
export function OrganizationMark({ organization, size = 'md', className = '' }: {
  organization: { name: string; photoUrl?: string | null | undefined };
  size?: 'sm' | 'md' | 'lg' | 'xl';
  className?: string;
}) {
  return (
    <span className={`cx-organization-mark is-${size}${organization.photoUrl ? ' has-photo' : ''} ${className}`.trim()} aria-hidden="true">
      {organization.photoUrl ? <img src={apiUrl(organization.photoUrl)} alt="" /> : organizationInitials(organization.name)}
    </span>
  );
}
