import {
  Breadcrumb,
  BreadcrumbButton,
  BreadcrumbDivider,
  BreadcrumbItem,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { Fragment, useEffect, useRef, type ReactNode } from 'react';

import { followLink } from '../router';

const useStyles = makeStyles({
  top: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
  title: {
    margin: 0,
    fontSize: tokens.fontSizeBase600,
    lineHeight: tokens.lineHeightBase600,
    fontWeight: tokens.fontWeightSemibold,
    ':focus-visible': { outlineOffset: '2px' },
  },
  actions: { display: 'flex', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  titleGroup: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM, flexWrap: 'wrap', minWidth: 0 },
});

export interface Crumb {
  label: string;
  path?: string;
}

/** Breadcrumb, h1 (focused when the page opens, so the change is announced), badges beside it and the page's actions. */
export function PageHeader({
  crumbs,
  title,
  actions,
  badges,
  titleClassName,
}: {
  crumbs: Crumb[];
  title: string;
  actions?: ReactNode;
  badges?: ReactNode;
  titleClassName?: string;
}) {
  const styles = useStyles();
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    heading.current?.focus();
  }, [title]);
  return (
    <>
      <Breadcrumb aria-label="Breadcrumb">
        {crumbs.map((crumb, index) => (
          <Fragment key={crumb.label}>
            {index > 0 ? <BreadcrumbDivider /> : null}
            <BreadcrumbItem>
              {crumb.path ? (
                <BreadcrumbButton href={crumb.path} onClick={(event) => followLink(event, crumb.path ?? '/')}>
                  {crumb.label}
                </BreadcrumbButton>
              ) : (
                <BreadcrumbButton current={index === crumbs.length - 1}>{crumb.label}</BreadcrumbButton>
              )}
            </BreadcrumbItem>
          </Fragment>
        ))}
      </Breadcrumb>
      <div className={styles.top}>
        <div className={styles.titleGroup}>
          <h1 ref={heading} tabIndex={-1} className={mergeClasses(styles.title, titleClassName)}>
            {title}
          </h1>
          {badges}
        </div>
        {actions ? (
          <div role="toolbar" aria-label="Page actions" className={styles.actions}>
            {actions}
          </div>
        ) : null}
      </div>
    </>
  );
}
