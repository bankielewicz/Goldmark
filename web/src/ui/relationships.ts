import { useCallback, useEffect, useRef, useState } from 'react';

import { api, type ApiError } from '../api';
import { asApiError, type ChangedBy, type RelationshipType } from './inventory';

/**
 * Relationships in the web app (slice 6; screen 5's Relationships tab; DEC-046): the API's shapes, a CI's two
 * sections loaded and paged, and the words the tab and the panel use. `from` is the dependent, `to` the
 * dependency. The boards are `docs/design/screens/slice-6/`.
 */

export type Direction = 'dependencies' | 'dependents';

export interface RelationshipEnd {
  id: string;
  name: string;
  type: string;
  lifecycleState: string;
}

export interface Relationship {
  id: string;
  type: string;
  from: RelationshipEnd;
  to: RelationshipEnd;
  redundancyGroup: string | null;
  /** When it was added ("Since", DEC-046 item 13). */
  createdAt: string | null;
  changedAt: string;
  changedBy: ChangedBy | null;
  eTag: string;
}

export interface RelationshipSection {
  items: Relationship[];
  /** With the first page only. */
  total: number | null;
  next: string | null;
}

export interface Relationships {
  dependencies: RelationshipSection;
  dependents: RelationshipSection;
}

/** A redundancy group the panel suggests: the groups on the dependent CI's relationships (DEC-046 item 2). */
export interface RedundancyGroupUse {
  name: string;
  relationships: number;
}

/** The built-in type whose dependency is a Team or a Person, which its one To end can't say (0006). */
export const ownedBy = 'owned_by';
export const ownerTypes = ['Team', 'Person'];

/** "Show N more" adds up to this many rows (DEC-046 item 12); the first page is 50. */
export const morePage = 200;

/** A CI's two sections, their first pages loaded with the page (the tab's count), then "Show N more". */
export function useRelationships(ciId: string): {
  data: Relationships | null;
  totals: { dependencies: number; dependents: number } | null;
  failure: ApiError | null;
  reload: () => Promise<void>;
  more: (direction: Direction) => Promise<void>;
  moreBusy: ReadonlySet<Direction>;
  moreFailure: { direction: Direction; error: ApiError } | null;
} {
  const [data, setData] = useState<Relationships | null>(null);
  const [totals, setTotals] = useState<{ dependencies: number; dependents: number } | null>(null);
  const [failure, setFailure] = useState<ApiError | null>(null);
  // Each section pages on its own; the ref keeps a second click from sending the same cursor again.
  const [moreBusy, setMoreBusy] = useState<ReadonlySet<Direction>>(new Set());
  const pending = useRef(new Set<Direction>());
  const [moreFailure, setMoreFailure] = useState<{ direction: Direction; error: ApiError } | null>(null);
  const request = useRef(0);

  const reload = useCallback(async () => {
    const current = ++request.current;
    setFailure(null);
    setMoreFailure(null);
    try {
      const { data: loaded } = await api<Relationships>('GET', `/api/v1/cis/${ciId}/relationships`);
      if (request.current !== current) return;
      setData(loaded);
      setTotals({ dependencies: loaded.dependencies.total ?? 0, dependents: loaded.dependents.total ?? 0 });
    } catch (error) {
      if (request.current === current) setFailure(asApiError(error));
    }
  }, [ciId]);

  useEffect(() => {
    setData(null);
    setTotals(null);
    void reload();
  }, [reload]);

  const more = useCallback(
    async (direction: Direction) => {
      const section = data?.[direction];
      if (!section?.next || pending.current.has(direction)) return;
      const current = request.current;
      pending.current.add(direction);
      setMoreBusy(new Set(pending.current));
      setMoreFailure((f) => (f?.direction === direction ? null : f));
      try {
        const query = new URLSearchParams({ direction, limit: String(morePage), cursor: section.next });
        const { data: page } = await api<RelationshipSection>(
          'GET',
          `/api/v1/cis/${ciId}/relationships?${query.toString()}`,
        );
        if (request.current !== current) return;
        setData((d) =>
          d
            ? {
                ...d,
                [direction]: {
                  items: [...d[direction].items, ...page.items],
                  total: d[direction].total,
                  next: page.next,
                },
              }
            : d,
        );
      } catch (error) {
        setMoreFailure({ direction, error: asApiError(error) });
      } finally {
        pending.current.delete(direction);
        setMoreBusy(new Set(pending.current));
      }
    },
    [ciId, data],
  );

  return { data, totals, failure, reload, more, moreBusy, moreFailure };
}

/** The end of a relationship type in words: "Any type" for none, "Team or Person" for owned_by's dependency. */
export function endText(type: RelationshipType, end: 'from' | 'to'): string {
  const name = type[end];
  if (name) return name;
  return end === 'to' && type.name === ownedBy ? ownerTypes.join(' or ') : 'Any type';
}

export function endsText(type: RelationshipType): string {
  return `${endText(type, 'from')} → ${endText(type, 'to')}`;
}

/** Whether a CI of this type can be the dependent (from) or the dependency (to) of the relationship type. */
export function sidesFor(type: RelationshipType, ciType: string): { dependent: boolean; dependency: boolean } {
  return {
    dependent: type.from === null || type.from === ciType,
    dependency: type.to === null ? type.name !== ownedBy || ownerTypes.includes(ciType) : type.to === ciType,
  };
}

/** The CI types the other end may have when the page's CI is on `side`; empty for any type. */
export function otherTypes(type: RelationshipType, side: 'dependent' | 'dependency'): string[] {
  if (side === 'dependency') return type.from ? [type.from] : [];
  if (type.to) return [type.to];
  return type.name === ownedBy ? ownerTypes : [];
}

/** How a row names a relationship, from the page's CI: "runs_on esx-mx-014", or "ERP depends_on app-erp-db01". */
export function relationshipLabel(relationship: Relationship, ciId: string): string {
  return relationship.from.id === ciId
    ? `${relationship.type} ${relationship.to.name}`
    : `${relationship.from.name} ${relationship.type} ${relationship.to.name}`;
}

/** "sw-core-03 and sw-core-04", "a, b and c". */
export function names(list: string[]): string {
  return list.length <= 1 ? (list[0] ?? '') : `${list.slice(0, -1).join(', ')} and ${list.at(-1) ?? ''}`;
}

/** The server's rule for a redundancy group (DEC-046 item 2): at most 200 characters once trimmed, no control characters. */
export function groupFits(text: string): boolean {
  return (
    text.trim().length <= 200 &&
    ![...text].some((ch) => {
      const code = ch.charCodeAt(0);
      return code <= 0x1f || (code >= 0x7f && code <= 0x9f);
    })
  );
}

/** A relationship field error from the API in the boards' words; `other` names the type the other CI must have. */
export function relationshipMessage(code: string, other: string[]): string {
  switch (code) {
    case 'Required':
      return 'Choose a CI.';
    case 'UnknownRelationshipType':
      return 'This relationship type was deleted meanwhile. Choose another.';
    case 'Self':
      return 'A CI can’t have a relationship with itself.';
    case 'WrongType':
      return other.length > 0
        ? `Choose a ${other.join(' or a ')}.`
        : 'Choose a CI of the type the relationship type names.';
    case 'CiRetired':
      return 'That CI was retired meanwhile. Choose another.';
    case 'CiNotFound':
    case 'InvalidId':
      return 'That CI no longer exists. Choose another.';
    case 'TooLong':
    case 'InvalidText':
      return 'Use up to 200 characters, without line breaks.';
    default:
      return 'This value isn’t valid.';
  }
}
