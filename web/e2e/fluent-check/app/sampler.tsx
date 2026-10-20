import { useState } from 'react';
import {
  Badge,
  Button,
  Checkbox,
  Combobox,
  createTableColumn,
  DataGrid,
  DataGridBody,
  DataGridCell,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridRow,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  DialogTrigger,
  DrawerBody,
  DrawerHeader,
  DrawerHeaderTitle,
  Field,
  FluentProvider,
  Input,
  Link,
  makeResetStyles,
  makeStaticStyles,
  makeStyles,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  mergeClasses,
  MessageBar,
  MessageBarBody,
  Option,
  OverlayDrawer,
  Popover,
  PopoverSurface,
  PopoverTrigger,
  Portal,
  Spinner,
  Switch,
  Tab,
  TabList,
  Toast,
  Toaster,
  ToastTitle,
  tokens,
  Tooltip,
  useId,
  useRestoreFocusTarget,
  useToastController,
  webDarkTheme,
  webLightTheme,
  type TableColumnDefinition,
} from '@fluentui/react-components';
import { CheckmarkCircle20Filled, ErrorCircle20Filled } from '@fluentui/react-icons';
import { CheckmarkCircle20Filled as CheckmarkCircleFontIcon } from '@fluentui/react-icons/fonts';

import { SelfCheck } from './selfcheck';

const useStatic = makeStaticStyles({
  '[data-static-probe]': { letterSpacing: '2px' },
});

const useReset = makeResetStyles({
  outlineWidth: '5px',
  outlineStyle: 'dotted',
  outlineColor: tokens.colorNeutralStroke1,
});

const useStyles = makeStyles({
  page: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalL,
  },
  probe: {
    borderTopWidth: '3px',
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorPaletteRedBorder2,
    '@media (min-width: 1px)': { paddingLeft: '7px' },
    ':hover': { color: tokens.colorBrandForeground1 },
  },
  fade: {
    animationName: { from: { opacity: 0.5 }, to: { opacity: 1 } },
    animationDuration: '10s',
    animationIterationCount: 'infinite',
  },
  // Griffel writes a mirrored rule for right-to-left documents.
  inlineStart: { paddingLeft: '11px' },
  row: { display: 'flex', columnGap: tokens.spacingHorizontalS, alignItems: 'center', flexWrap: 'wrap' },
});

interface Item {
  id: string;
  name: string;
  status: 'Healthy' | 'Down';
}

const items: Item[] = [
  { id: '1', name: 'sql-prod-01', status: 'Healthy' },
  { id: '2', name: 'esx-blade-07', status: 'Down' },
];

const columns: TableColumnDefinition<Item>[] = [
  createTableColumn<Item>({
    columnId: 'name',
    compare: (a, b) => a.name.localeCompare(b.name),
    renderHeaderCell: () => 'Name',
    renderCell: (item) => item.name,
  }),
  createTableColumn<Item>({
    columnId: 'status',
    compare: (a, b) => a.status.localeCompare(b.status),
    renderHeaderCell: () => 'Status',
    renderCell: (item) =>
      item.status === 'Healthy' ? (
        <span>
          <CheckmarkCircle20Filled /> Healthy
        </span>
      ) : (
        <span>
          <ErrorCircle20Filled /> Down
        </span>
      ),
  }),
];

/** Griffel picks the mirrored class from the text direction where useStyles() runs. */
function RtlProbe() {
  const styles = useStyles();
  return (
    <div data-testid="rtl-probe" className={styles.inlineStart}>
      نص من اليمين إلى اليسار
    </div>
  );
}

/** The outer theme can be switched after mount, which rewrites FluentProvider's theme rule. */
export function SamplerApp() {
  const [dark, setDark] = useState(false);
  return (
    <FluentProvider theme={dark ? webDarkTheme : webLightTheme} data-testid="provider">
      <Sampler onSwitchTheme={() => setDark((d) => !d)} />
    </FluentProvider>
  );
}

function Sampler({ onSwitchTheme }: { onSwitchTheme: () => void }) {
  useStatic();
  const styles = useStyles();
  const reset = useReset();
  const toasterId = useId('toaster');
  const { dispatchToast } = useToastController(toasterId);
  const [tab, setTab] = useState('attributes');
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [portalMount, setPortalMount] = useState<HTMLElement | null>(null);
  // Fluent's documented way for a drawer to give focus back to what opened it.
  const restoreFocusTarget = useRestoreFocusTarget();

  return (
    <main className={styles.page}>
      <h1>Fluent UI check</h1>
      <SelfCheck />
      {/* Expected colours, applied through React's style prop (CSSOM, allowed by the CSP). */}
      <span data-testid="brand-reference" style={{ backgroundColor: webLightTheme.colorBrandBackground }} />
      <span data-testid="light-reference" style={{ backgroundColor: webLightTheme.colorNeutralBackground1 }} />
      <span data-testid="dark-reference" style={{ backgroundColor: webDarkTheme.colorNeutralBackground1 }} />
      <div className={styles.row}>
        <Button appearance="primary" data-testid="primary-button">
          Primary
        </Button>
        <Button onClick={onSwitchTheme}>Switch theme</Button>
        <Badge appearance="filled" color="danger">
          Down
        </Badge>
        <Link href="#top">Link</Link>
        <Spinner size="tiny" label="Loading" />
        <span data-testid="font-icon">
          <CheckmarkCircleFontIcon />
        </span>
      </div>
      <div data-testid="make-styles-probe" className={mergeClasses(styles.probe, styles.fade)}>
        makeStyles probe
      </div>
      <div data-testid="reset-probe" className={reset}>
        makeResetStyles probe
      </div>
      <div data-testid="static-probe" data-static-probe="">
        makeStaticStyles probe
      </div>
      <div data-testid="nonce-control">nonce control</div>
      <Field label="Username" validationMessage="Required" validationState="error">
        <Input />
      </Field>
      <div className={styles.row}>
        <Checkbox label="Remember" />
        <Switch label="Enabled" />
      </div>
      <Field label="CI type">
        <Combobox placeholder="Pick a type">
          <Option>Server</Option>
          <Option>VirtualMachine</Option>
        </Combobox>
      </Field>
      <TabList selectedValue={tab} onTabSelect={(_, data) => setTab(String(data.value))}>
        <Tab value="attributes">Attributes</Tab>
        <Tab value="history">History</Tab>
      </TabList>
      <MessageBar intent="warning">
        <MessageBarBody>Collector is stale.</MessageBarBody>
      </MessageBar>
      <div className={styles.row}>
        <Menu>
          <MenuTrigger disableButtonEnhancement>
            <Button>Open menu</Button>
          </MenuTrigger>
          <MenuPopover data-testid="menu-popover">
            <MenuList>
              <MenuItem>Edit</MenuItem>
              <MenuItem>Retire</MenuItem>
            </MenuList>
          </MenuPopover>
        </Menu>
        <Popover>
          <PopoverTrigger disableButtonEnhancement>
            <Button>Open popover</Button>
          </PopoverTrigger>
          <PopoverSurface data-testid="popover-surface">Popover content</PopoverSurface>
        </Popover>
        <Tooltip content="Tooltip content" relationship="description">
          <Button>Tooltip target</Button>
        </Tooltip>
        <Dialog>
          <DialogTrigger disableButtonEnhancement>
            <Button>Open dialog</Button>
          </DialogTrigger>
          <DialogSurface data-testid="dialog-surface">
            <DialogBody>
              <DialogTitle>Retire CI</DialogTitle>
              <DialogContent>
                Retire sql-prod-01?
                <Input aria-label="Reason" />
              </DialogContent>
              <DialogActions>
                <DialogTrigger disableButtonEnhancement>
                  <Button appearance="secondary">Cancel</Button>
                </DialogTrigger>
                <Button appearance="primary">Retire</Button>
              </DialogActions>
            </DialogBody>
          </DialogSurface>
        </Dialog>
        <Button {...restoreFocusTarget} onClick={() => setDrawerOpen(true)}>
          Open drawer
        </Button>
        <Button
          onClick={() =>
            dispatchToast(
              <Toast data-testid="toast">
                <ToastTitle>Saved</ToastTitle>
              </Toast>,
              { intent: 'success' },
            )
          }
        >
          Show toast
        </Button>
      </div>
      <OverlayDrawer open={drawerOpen} onOpenChange={(_, data) => setDrawerOpen(data.open)} data-testid="drawer">
        <DrawerHeader>
          <DrawerHeaderTitle>CI details</DrawerHeaderTitle>
        </DrawerHeader>
        <DrawerBody>
          <Input aria-label="Drawer field" />
          <Button onClick={() => setDrawerOpen(false)}>Close drawer</Button>
        </DrawerBody>
      </OverlayDrawer>
      <DataGrid items={items} columns={columns} getRowId={(item: Item) => item.id} sortable>
        <DataGridHeader>
          <DataGridRow>
            {({ renderHeaderCell }) => <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>}
          </DataGridRow>
        </DataGridHeader>
        <DataGridBody<Item>>
          {({ item, rowId }) => (
            <DataGridRow<Item> key={rowId}>
              {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
            </DataGridRow>
          )}
        </DataGridBody>
      </DataGrid>
      <Toaster toasterId={toasterId} />
      <div data-testid="portal-mount" ref={setPortalMount} />
      {portalMount && (
        <Portal mountNode={portalMount}>
          <div data-testid="portal-probe" className={styles.probe}>
            Portal in a custom mount node
          </div>
        </Portal>
      )}
      <FluentProvider theme={webDarkTheme} data-testid="dark-provider">
        Nested dark theme
        <Menu>
          <MenuTrigger disableButtonEnhancement>
            <Button>Open dark menu</Button>
          </MenuTrigger>
          <MenuPopover data-testid="dark-menu-popover">
            <MenuList>
              <MenuItem>Dark item</MenuItem>
            </MenuList>
          </MenuPopover>
        </Menu>
      </FluentProvider>
      <FluentProvider dir="rtl" data-testid="rtl-provider">
        <RtlProbe />
        <Popover>
          <PopoverTrigger disableButtonEnhancement>
            <Button>Open RTL popover</Button>
          </PopoverTrigger>
          <PopoverSurface data-testid="rtl-popover-surface">RTL popover</PopoverSurface>
        </Popover>
      </FluentProvider>
    </main>
  );
}
