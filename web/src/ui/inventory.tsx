import { Badge } from '@fluentui/react-components';
import {
  AppsListRegular,
  BoxMultipleRegular,
  BoxRegular,
  BriefcaseRegular,
  CubeMultipleRegular,
  CubeRegular,
  DatabaseMultipleRegular,
  DatabaseRegular,
  DesktopRegular,
  GlobeRegular,
  HardDriveRegular,
  LocationRegular,
  OrganizationRegular,
  PeopleTeamRegular,
  PersonRegular,
  RouterRegular,
  ServerMultipleRegular,
  ServerRegular,
  StorageRegular,
  TableRegular,
} from '@fluentui/react-icons';
import { useCallback, useEffect, useState, type ReactElement } from 'react';

import { api, ApiError, type SessionUser } from '../api';

/**
 * CIs and their types in the web app (slice 5; screens 4 and 5; DEC-044): the API's shapes, the types
 * loaded once per page, the type icons and badges, and attribute values in words. The boards are
 * `docs/design/screens/slice-5/`.
 */

export type AttributeKind = 'string' | 'number' | 'bool' | 'date' | 'enum' | 'ciRef';

export interface AttributeDef {
  name: string;
  label: string;
  dataType: AttributeKind;
  required: boolean;
  choices: string[] | null;
  refType: string | null;
  builtIn: boolean;
}

export interface CiType {
  id: number;
  name: string;
  category: string | null;
  icon: string | null;
  builtIn: boolean;
  attributes: AttributeDef[];
}

export interface CiSummary {
  id: string;
  name: string;
  type: string;
  lifecycleState: string;
  changedAt: string;
}

export interface CiPageData {
  items: CiSummary[];
  total: number | null;
  types: { type: string; count: number }[] | null;
  next: string | null;
}

export interface CiReference {
  id: string;
  name: string;
  type: string;
  lifecycleState: string;
}

export interface CiDetail {
  id: string;
  name: string;
  type: string;
  lifecycleState: string;
  attributes: Record<string, unknown>;
  references: Record<string, CiReference>;
  changedAt: string;
  changedBy: { id: string | null; displayName: string | null } | null;
  eTag: string;
}

/** The §9.3 groups in their order; a custom type without one comes last (slice 5b). */
export const categories = ['Location & ownership', 'Compute', 'Network', 'Storage', 'Clustering & SQL', 'Business'];

/** Admins and Editors create, change, retire and restore CIs (US-11; the API's CiEditor policy). */
export function canEditCis(user: SessionUser | null): boolean {
  return (user?.roles.includes('Admin') || user?.roles.includes('Editor')) ?? false;
}

/** A CI ID as the API makes them (UUID, version 7 in practice); anything else is "not a CI ID". */
export function isCiId(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}

export function asApiError(error: unknown): ApiError {
  return error instanceof ApiError ? error : new ApiError(0, 'unexpected', 'Something went wrong on this page.');
}

/** The CI types with their attributes, read when a page opens (an Admin may add an attribute at any time). */
export function useCiTypes(): { types: CiType[] | null; failure: ApiError | null; reload: () => void } {
  const [types, setTypes] = useState<CiType[] | null>(null);
  const [failure, setFailure] = useState<ApiError | null>(null);
  const load = useCallback(() => {
    setFailure(null);
    api<{ types: CiType[] }>('GET', '/api/v1/types')
      .then(({ data }) => setTypes(data.types))
      .catch((error: unknown) => setFailure(asApiError(error)));
  }, []);
  useEffect(load, [load]);
  return { types, failure, reload: load };
}

const icons: Record<string, ReactElement> = {
  location: <LocationRegular />,
  team: <PeopleTeamRegular />,
  person: <PersonRegular />,
  'hypervisor-manager': <OrganizationRegular />,
  'hypervisor-cluster': <ServerMultipleRegular />,
  'hypervisor-host': <CubeMultipleRegular />,
  chassis: <BoxMultipleRegular />,
  'virtual-machine': <DesktopRegular />,
  server: <ServerRegular />,
  appliance: <BoxRegular />,
  'network-device': <RouterRegular />,
  datastore: <HardDriveRegular />,
  'storage-group': <CubeRegular />,
  'storage-array': <StorageRegular />,
  'windows-cluster': <AppsListRegular />,
  'cluster-network-name': <GlobeRegular />,
  'sql-instance': <DatabaseRegular />,
  'sql-availability-group': <DatabaseMultipleRegular />,
  'sql-database': <TableRegular />,
  'business-service': <BriefcaseRegular />,
};

/** A type's icon, by its key or, before the types have loaded, by the built-in type's name. */
export function TypeIcon({ type, types }: { type: string; types: CiType[] | null }) {
  const key = types?.find((t) => t.name === type)?.icon ?? type.replace(/(?<=[a-z])(?=[A-Z])/g, '-').toLowerCase();
  return <span aria-hidden="true">{icons[key] ?? <CubeRegular />}</span>;
}

export function TypeBadge({ type, types }: { type: string; types: CiType[] | null }) {
  return (
    <Badge appearance="tint" color="subtle" size="large" icon={<TypeIcon type={type} types={types} />}>
      {type}
    </Badge>
  );
}

/** Lifecycle as the badge vocabulary has it: Active success, Retired subtle (slice 5 sets only these two). */
export function LifecycleBadge({ state }: { state: string }) {
  const color =
    state === 'Active' ? 'success' : state === 'Retiring' ? 'severe' : state === 'Planned' ? 'informative' : 'subtle';
  return (
    <Badge appearance="tint" color={color}>
      {state}
    </Badge>
  );
}

/** An attribute value in words; null for "Not set". A link's name comes from the CI's references. */
export function valueText(
  attribute: AttributeDef,
  value: unknown,
  references: Record<string, CiReference>,
): string | null {
  if (value === null || value === undefined || value === '') return null;
  switch (attribute.dataType) {
    case 'bool':
      return value === true ? 'Yes' : 'No';
    case 'ciRef':
      return typeof value === 'string' ? (references[value]?.name ?? value) : null;
    default:
      return typeof value === 'number' ? String(value) : typeof value === 'string' ? value : JSON.stringify(value);
  }
}

/** A field error from the API in the boards' words. */
export function fieldMessage(code: string, attribute: AttributeDef | null): string {
  const label = attribute?.label.toLowerCase() ?? 'a name';
  switch (code) {
    case 'Required':
      return attribute ? `Enter ${article(label)}${label}.` : 'Enter a name.';
    case 'TooLong':
      return attribute ? 'This is too long (4,000 characters at most).' : 'Names are 400 characters at most.';
    case 'InvalidName':
    case 'InvalidText':
      return 'Remove the line breaks and other control characters.';
    case 'InvalidNumber':
      return 'Enter a number.';
    case 'TooPrecise':
      return 'This has more digits than Goldmark keeps exactly (28 significant digits). Round it.';
    case 'NumberOutOfRange':
      return 'This number is too large: Goldmark keeps numbers up to 79,228,162,514,264,337,593,543,950,335.';
    case 'InvalidDate':
      return 'Enter a date.';
    case 'InvalidChoice':
      return 'Choose one of the listed values.';
    case 'InvalidBool':
      return 'Choose Yes, No or Not set.';
    case 'LinkedCiRetired':
      return 'Choose a CI that isn’t retired.';
    case 'LinkedCiSelf':
      return 'A CI can’t link to itself. Choose another CI.';
    case 'LinkedCiNotFound':
      return 'That CI no longer exists. Choose another.';
    case 'LinkedCiWrongType':
      return `Choose a ${attribute?.refType ?? 'CI'}.`;
    case 'UnknownAttribute':
      return 'This attribute was removed from the type. Reload the page.';
    default:
      return 'This value isn’t valid.';
  }
}

function article(word: string): string {
  return /^[aeiou]/.test(word) ? 'an ' : 'a ';
}

/** A Toast the next page shows once (the panel closes, the CI's page opens, then "… created"). */
let pendingToast: string | null = null;

export function setPendingToast(text: string): void {
  pendingToast = text;
}

export function takePendingToast(): string | null {
  const text = pendingToast;
  pendingToast = null;
  return text;
}
