import { CalendarDays, History, Layers, MapPin, Pencil, Printer, ShieldCheck } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Outlet, useLocation, useParams } from 'react-router-dom';

import MxButton from '../../../components/ui/Button';
import PageHeader from '../../../components/ui/PageHeader';
import { Panel } from '../../../components/ui/Panel';
import { StatusPill } from '../../../components/ui/Pill';
import Skeleton from '../../../components/ui/Skeleton';
import { EmptyState, ErrorState } from '../../../components/ui/States';
import useAuth from '../../auth/hooks/useAuth';
import { ADMIN_ROLES, hasRole } from '../../auth/services/roles';
import AssetLabel from '../components/AssetLabel';
import HistoryFeed from '../components/HistoryFeed';
import SummaryCard from '../components/SummaryCard';
import WarrantyPill from '../components/WarrantyPill';
import useAsset from '../hooks/useAsset';
import useFailureSummary from '../hooks/useFailureSummary';
import { formatDateOnly, roomLabel } from '../services/assetsApi';
import styles from '../assets.module.css';

/** Print the label card and nothing else — see the print rules in tokens.css. */
function printLabel() {
  document.body.classList.add('mx-printing-label');
  const done = () => {
    document.body.classList.remove('mx-printing-label');
    window.removeEventListener('afterprint', done);
  };
  window.addEventListener('afterprint', done);
  window.print();
}

function DetailSkeleton() {
  return (
    <div role="status" aria-label="Loading asset">
      <Skeleton width={180} height={12} style={{ marginBottom: 18 }} />
      <Skeleton width="46%" height={40} style={{ marginBottom: 12 }} />
      <Skeleton width="28%" height={14} style={{ marginBottom: 28 }} />
      <div className={styles.detailGrid}>
        <Skeleton height={420} radius={20} />
        <div className={styles.stack}>
          <Skeleton height={220} radius={18} />
          <Skeleton height={260} radius={20} />
        </div>
      </div>
    </div>
  );
}

function Fact({ icon: Icon, label, value }) {
  return (
    <div className={styles.factCell}>
      <dt>
        <Icon aria-hidden="true" strokeWidth={1.7} />
        {label}
      </dt>
      <dd>{value}</dd>
    </div>
  );
}

/**
 * One asset. Two requests, each with its own states: the asset is the page, and a failed
 * summary is an error in its panel while the history — the evidence the summary was computed
 * from — still renders.
 */
function AssetDetailBody({ id }) {
  const asset = useAsset(id);
  const summary = useFailureSummary(id);
  const { role } = useAuth();
  const isAdmin = hasRole(role, ADMIN_ROLES);

  if (asset.isLoading) return <DetailSkeleton />;

  if (asset.error) {
    return (
      <>
        <PageHeader crumbs={[{ label: 'Assets', to: '/assets' }, { label: `#${id}` }]} title="Asset" />
        <Panel>
          <ErrorState
            title={asset.error.status === 404 ? 'Asset not found' : 'Could not load this asset'}
            message={asset.error.status === 404 ? `There is no asset with id ${id} in the registry.` : asset.error.message}
            action={<MxButton to="/assets">Back to all assets</MxButton>}
          />
        </Panel>
      </>
    );
  }

  const data = asset.data;
  const makeModel = [data.manufacturer, data.model].filter(Boolean).join(' ');

  return (
    <>
      <PageHeader
        crumbs={[{ label: 'Assets', to: '/assets' }, { label: data.assetTag }]}
        title={data.name}
        lead={`${data.category.name}${makeModel ? ` · ${makeModel}` : ''}`}
        actions={
          <>
            <MxButton icon={Printer} onClick={printLabel}>
              Print label
            </MxButton>
            {isAdmin ? (
              <MxButton variant="primary" icon={Pencil} to={`/assets/${data.id}/edit`}>
                Edit asset
              </MxButton>
            ) : null}
          </>
        }
      >
        <div className={styles.headerPills}>
          <StatusPill status={data.status} />
          <WarrantyPill
            isUnderWarranty={summary.data?.isUnderWarranty}
            warrantyExpiresOn={data.warrantyExpiresOn}
            isLoading={summary.isLoading}
            hasError={Boolean(summary.error)}
          />
        </div>
      </PageHeader>

      <dl className={styles.factStrip}>
        <Fact icon={MapPin} label="Room" value={roomLabel(data.room)} />
        <Fact icon={Layers} label="Floor" value={<span className="mx-mono">{data.room.floor}</span>} />
        <Fact icon={CalendarDays} label="Installed" value={<span className="mx-mono">{formatDateOnly(data.installedOn)}</span>} />
        <Fact
          icon={ShieldCheck}
          label="Warranty until"
          value={
            data.warrantyExpiresOn ? <span className="mx-mono">{formatDateOnly(data.warrantyExpiresOn)}</span> : 'Not recorded'
          }
        />
      </dl>

      <div className={styles.detailGrid}>
        <Panel
          eyebrow="Service history"
          count={data.serviceHistory.length || null}
          actions={<span className={styles.panelHint}>Oldest first · notes verbatim</span>}
        >
          {/* An empty history is not a failure and must not look like one. */}
          {data.serviceHistory.length === 0 ? (
            <EmptyState
              icon={History}
              title="No service visits on record"
              body="Visits appear here as completed work orders are closed against this asset."
            />
          ) : (
            <HistoryFeed records={data.serviceHistory} />
          )}
        </Panel>

        <aside className={styles.detailAside}>
          <AssetLabel tag={data.assetTag} name={data.name} location={roomLabel(data.room)} printable />

          {summary.isLoading ? <Skeleton height={260} radius={20} /> : null}
          {!summary.isLoading && summary.error ? (
            <Panel eyebrow="Failure summary">
              <ErrorState compact title="Could not load the failure summary" message={summary.error.message} />
            </Panel>
          ) : null}
          {!summary.isLoading && !summary.error && summary.data ? <SummaryCard summary={summary.data} /> : null}
        </aside>
      </div>
    </>
  );
}

export function AssetDetailPage() {
  const { id } = useParams();
  const location = useLocation();

  // The edit panel navigates back here with `state.refresh` after a save. Bumping `version`
  // remounts the body, and a fresh mount is a fresh useFetch — the shared hook has no refetch.
  // Opening the panel again (no state) leaves `version` alone, so the page does not reload.
  const [version, setVersion] = useState(0);
  const refresh = location.state?.refresh;
  useEffect(() => {
    // eslint-disable-next-line react/set-state-in-effect
    if (refresh) setVersion((current) => current + 1);
  }, [refresh]);

  return (
    <section className={styles.page}>
      <AssetDetailBody key={`${id}-${version}`} id={id} />
      {/* /assets/:id/edit renders its slide-over here, over the detail. */}
      <Outlet />
    </section>
  );
}

export default AssetDetailPage;
