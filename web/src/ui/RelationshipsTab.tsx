import {
  Badge,
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Link,
  Skeleton,
  SkeletonItem,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Text,
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { AddRegular, DeleteRegular, EditRegular } from '@fluentui/react-icons';
import { useState } from 'react';

import { api, type ApiError } from '../api';
import { followLink } from '../router';
import { ErrorBar, Notice } from './common';
import { asApiError, TypeIcon, type CiDetail, type CiType, type RelationshipType } from './inventory';
import {
  relationshipLabel,
  type Direction,
  type Relationship,
  type RelationshipSection,
  type useRelationships,
} from './relationships';

const useStyles = makeStyles({
  tab: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL },
  toolbar: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM },
  section: {
    backgroundColor: tokens.colorNeutralBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
    display: 'flex',
    flexDirection: 'column',
  },
  sectionTitle: {
    margin: 0,
    padding: `14px ${tokens.spacingHorizontalL} 6px`,
    fontSize: tokens.fontSizeBase400,
    lineHeight: tokens.lineHeightBase400,
    fontWeight: tokens.fontWeightSemibold,
  },
  scroll: { overflowX: 'auto' },
  table: { minWidth: '900px' },
  caption: {
    position: 'absolute',
    width: '1px',
    height: '1px',
    overflow: 'hidden',
    clip: 'rect(0 0 0 0)',
  },
  typeColumn: { width: '170px' },
  ciTypeColumn: { width: '170px' },
  groupColumn: { width: '150px' },
  narrowColumn: { width: '100px' },
  actionsColumn: { width: '88px' },
  type: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    borderRadius: tokens.borderRadiusSmall,
    ':focus-visible': { outline: `${tokens.strokeWidthThick} solid ${tokens.colorStrokeFocus2}` },
  },
  related: { display: 'inline-flex', alignItems: 'center', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  link: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    fontWeight: tokens.fontWeightSemibold,
  },
  muted: { color: tokens.colorNeutralForeground2, whiteSpace: 'nowrap' },
  since: { color: tokens.colorNeutralForeground2, whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' },
  actions: { display: 'flex', justifyContent: 'flex-end', gap: '2px' },
  more: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
  },
  empty: {
    backgroundColor: tokens.colorNeutralBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
    padding: tokens.spacingHorizontalXL,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-start',
    gap: tokens.spacingVerticalM,
  },
  emptyText: {
    margin: 0,
    fontSize: tokens.fontSizeBase400,
    lineHeight: tokens.lineHeightBase400,
    fontWeight: tokens.fontWeightSemibold,
  },
  danger: {
    backgroundColor: tokens.colorStatusDangerBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
    ':hover': {
      backgroundColor: tokens.colorStatusDangerBackground3Hover,
      color: tokens.colorNeutralForegroundOnBrand,
    },
    ':hover:active': {
      backgroundColor: tokens.colorStatusDangerBackground3Pressed,
      color: tokens.colorNeutralForegroundOnBrand,
    },
  },
});

/**
 * Screen 5's Relationships tab (slice 6 boards `Main`, `Rels*`, `DeleteRel*`; DEC-046): what the CI depends on and
 * what depends on it, each sorted by relationship type then the related CI's name, 50 rows then "Show N more";
 * retired CIs are listed, marked Retired. Admins and Editors add (not on a retired CI), edit the redundancy group and
 * delete, the delete confirmed first.
 */
export function RelationshipsTab({
  ci,
  relationships,
  canEdit,
  types,
  relationshipTypes,
  onAdd,
  onEdit,
  onDeleted,
}: {
  ci: CiDetail;
  relationships: ReturnType<typeof useRelationships>;
  canEdit: boolean;
  types: CiType[] | null;
  relationshipTypes: RelationshipType[] | null;
  onAdd: () => void;
  onEdit: (relationship: Relationship) => void;
  onDeleted: () => void;
}) {
  const styles = useStyles();
  const { data, totals, failure, reload, more, moreBusy, moreFailure } = relationships;
  const [deleting, setDeleting] = useState<Relationship | null>(null);
  const [busy, setBusy] = useState(false);
  const [deleteFailure, setDeleteFailure] = useState<ApiError | null>(null);
  const [gone, setGone] = useState<string | null>(null);
  const retired = ci.lifecycleState === 'Retired';
  const total = totals ? totals.dependencies + totals.dependents : 0;

  const remove = async () => {
    if (!deleting) return;
    setBusy(true);
    setDeleteFailure(null);
    setGone(null);
    try {
      await api('DELETE', `/api/v1/relationships/${deleting.id}`, undefined, { ifMatch: `"${deleting.eTag}"` });
      setDeleting(null);
      onDeleted();
    } catch (error) {
      const problem = asApiError(error);
      if (problem.status === 404 || problem.status === 412) {
        setGone(
          problem.status === 404
            ? `${relationshipLabel(deleting, ci.id)} was already deleted.`
            : `${relationshipLabel(deleting, ci.id)} changed meanwhile, so it wasn’t deleted. Check it and try again.`,
        );
        setDeleting(null);
        void reload();
      } else {
        setDeleteFailure(problem);
      }
    } finally {
      setBusy(false);
    }
  };

  if (failure && !data) {
    return (
      <div className={styles.tab}>
        <ErrorBar
          error={failure}
          message={
            failure.status === 0
              ? 'Couldn’t load the relationships. Check your connection, then try again.'
              : 'Couldn’t load the relationships: something went wrong on the server.'
          }
          onRetry={() => void reload()}
        />
      </div>
    );
  }

  if (!data) {
    return (
      <div className={styles.tab} aria-busy="true">
        {[3, 4].map((rows, i) => (
          <div key={i} className={styles.section}>
            <Skeleton aria-label="Loading the relationships">
              {Array.from({ length: rows }, (_, j) => (
                <SkeletonItem key={j} size={16} />
              ))}
            </Skeleton>
          </div>
        ))}
      </div>
    );
  }

  const description = (name: string) => relationshipTypes?.find((t) => t.name === name)?.description ?? '';

  return (
    <div className={styles.tab}>
      {gone ? <Notice intent="warning">{gone}</Notice> : null}
      {total === 0 ? (
        <div className={styles.empty}>
          <p className={styles.emptyText}>{ci.name} has no relationships yet.</p>
          {canEdit && !retired ? (
            <Button icon={<AddRegular />} aria-haspopup="dialog" onClick={onAdd}>
              Add relationship
            </Button>
          ) : null}
        </div>
      ) : (
        <>
          {canEdit && !retired ? (
            <div className={styles.toolbar}>
              <Button icon={<AddRegular />} aria-haspopup="dialog" onClick={onAdd}>
                Add relationship
              </Button>
            </div>
          ) : null}
          {(['dependencies', 'dependents'] as const).map((direction) => (
            <Section
              key={direction}
              ci={ci}
              direction={direction}
              section={data[direction]}
              total={totals?.[direction] ?? 0}
              canEdit={canEdit}
              types={types}
              description={description}
              busy={moreBusy.has(direction)}
              onMore={() => void more(direction)}
              onEdit={onEdit}
              onDelete={(relationship) => {
                setDeleteFailure(null);
                setDeleting(relationship);
              }}
            />
          ))}
          {moreFailure ? <ErrorBar error={moreFailure.error} onRetry={() => void more(moreFailure.direction)} /> : null}
        </>
      )}

      <Dialog
        open={deleting !== null}
        modalType="alert"
        onOpenChange={(_, d) => (d.open || busy ? undefined : setDeleting(null))}
      >
        <DialogSurface aria-describedby="delete-relationship-body">
          <DialogBody>
            <DialogTitle>Delete {deleting ? relationshipLabel(deleting, ci.id) : ''}?</DialogTitle>
            <DialogContent id="delete-relationship-body">
              {deleting
                ? `${deleting.from.name} will no longer depend on ${deleting.to.name}. The history of both CIs keeps the relationship.`
                : null}
              {deleteFailure ? <ErrorBar error={deleteFailure} onRetry={() => void remove()} /> : null}
            </DialogContent>
            <DialogActions>
              <Button disabled={busy} onClick={() => setDeleting(null)}>
                Cancel
              </Button>
              <Button
                appearance="primary"
                className={busy ? undefined : styles.danger}
                disabled={busy}
                onClick={() => void remove()}
              >
                {busy ? <Spinner size="tiny" aria-label="Deleting" /> : null}
                Delete
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
}

function Section({
  ci,
  direction,
  section,
  total,
  canEdit,
  types,
  description,
  busy,
  onMore,
  onEdit,
  onDelete,
}: {
  ci: CiDetail;
  direction: Direction;
  section: RelationshipSection;
  total: number;
  canEdit: boolean;
  types: CiType[] | null;
  description: (type: string) => string;
  busy: boolean;
  onMore: () => void;
  onEdit: (relationship: Relationship) => void;
  onDelete: (relationship: Relationship) => void;
}) {
  const styles = useStyles();
  const outgoing = direction === 'dependencies';
  const title = outgoing ? 'Depends on' : 'Depended on by';
  const id = outgoing ? 'rel-out' : 'rel-in';
  const rest = total - section.items.length;
  return (
    <section aria-labelledby={id} className={styles.section}>
      <h2 id={id} className={styles.sectionTitle}>
        {title} ({total.toLocaleString('en-US')})
      </h2>
      <div className={styles.scroll}>
        <Table className={styles.table} size="medium">
          <caption className={styles.caption}>
            {title}: {outgoing ? `the CIs ${ci.name} depends on` : `the CIs that depend on ${ci.name}`}
          </caption>
          <TableHeader>
            <TableRow>
              <TableHeaderCell className={styles.typeColumn}>Relationship</TableHeaderCell>
              <TableHeaderCell>Related CI</TableHeaderCell>
              <TableHeaderCell className={styles.ciTypeColumn}>CI type</TableHeaderCell>
              <TableHeaderCell className={styles.groupColumn}>Redundancy group</TableHeaderCell>
              <TableHeaderCell className={styles.narrowColumn}>Source</TableHeaderCell>
              <TableHeaderCell className={styles.narrowColumn}>Since</TableHeaderCell>
              {canEdit ? (
                <TableHeaderCell className={styles.actionsColumn}>
                  <span className={styles.caption}>Actions</span>
                </TableHeaderCell>
              ) : null}
            </TableRow>
          </TableHeader>
          <TableBody>
            {section.items.length === 0 ? (
              <TableRow>
                <TableCell colSpan={canEdit ? 7 : 6} className={styles.muted}>
                  {outgoing ? `${ci.name} depends on no CIs.` : `No CIs depend on ${ci.name}.`}
                </TableCell>
              </TableRow>
            ) : null}
            {section.items.map((relationship) => {
              const related = outgoing ? relationship.to : relationship.from;
              const label = relationshipLabel(relationship, ci.id);
              const text = description(relationship.type);
              return (
                <TableRow key={relationship.id}>
                  <TableCell>
                    {text ? (
                      <Tooltip content={text} relationship="description">
                        <span tabIndex={0} className={styles.type}>
                          {relationship.type}
                        </span>
                      </Tooltip>
                    ) : (
                      <span className={styles.type}>{relationship.type}</span>
                    )}
                  </TableCell>
                  <TableCell>
                    <span className={styles.related}>
                      <Link
                        href={`/ci/${related.id}`}
                        className={styles.link}
                        onClick={(event) => followLink(event, `/ci/${related.id}`)}
                      >
                        <TypeIcon type={related.type} types={types} />
                        {related.name}
                      </Link>
                      {related.lifecycleState === 'Retired' ? (
                        <Badge appearance="tint" color="subtle">
                          Retired
                        </Badge>
                      ) : null}
                    </span>
                  </TableCell>
                  <TableCell className={styles.muted}>{related.type}</TableCell>
                  <TableCell className={styles.muted}>{relationship.redundancyGroup ?? '—'}</TableCell>
                  <TableCell className={styles.muted}>Manual</TableCell>
                  <TableCell className={styles.since}>
                    {relationship.createdAt ? relationship.createdAt.slice(0, 10) : '—'}
                  </TableCell>
                  {canEdit ? (
                    <TableCell>
                      <div className={styles.actions}>
                        <Tooltip content={`Edit ${label}`} relationship="label">
                          <Button
                            appearance="subtle"
                            icon={<EditRegular />}
                            aria-haspopup="dialog"
                            onClick={() => onEdit(relationship)}
                          />
                        </Tooltip>
                        <Tooltip content={`Delete ${label}`} relationship="label">
                          <Button
                            appearance="subtle"
                            icon={<DeleteRegular />}
                            aria-haspopup="dialog"
                            onClick={() => onDelete(relationship)}
                          />
                        </Tooltip>
                      </div>
                    </TableCell>
                  ) : null}
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>
      {rest > 0 && section.next ? (
        <div className={styles.more}>
          <Button disabled={busy} onClick={onMore}>
            {busy ? <Spinner size="tiny" aria-label="Loading more" /> : null}
            Show {Math.min(rest, 200).toLocaleString('en-US')} more
          </Button>
          <Text>
            Showing {section.items.length.toLocaleString('en-US')} of {total.toLocaleString('en-US')}
          </Text>
        </div>
      ) : null}
    </section>
  );
}
