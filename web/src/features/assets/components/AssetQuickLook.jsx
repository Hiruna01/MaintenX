import { Pencil } from 'lucide-react';

import MxButton from '../../../components/ui/Button';
import { Panel } from '../../../components/ui/Panel';
import { StatusPill } from '../../../components/ui/Pill';
import Skeleton from '../../../components/ui/Skeleton';
import SlideOver from '../../../components/ui/SlideOver';
import { EmptyState, ErrorState } from '../../../components/ui/States';
import useAsset from '../hooks/useAsset';
import useFailureSummary from '../hooks/useFailureSummary';
import { formatDateOnly, roomLabel } from '../services/assetsApi';
import styles from '../assets.module.css';
import AssetLabel from './AssetLabel';
import HistoryFeed from './HistoryFeed';
import SummaryCard from './SummaryCard';
import TagChip from './TagChip';
import WarrantyPill from './WarrantyPill';

/**
 * A quick look at one asset without leaving the catalogue — the same two requests the detail
 * page makes, each with its own states. "Open full" goes to /assets/:id for the whole history.
 */
export function AssetQuickLook({ assetId, open, onOpenChange, canEdit }) {
  const asset = useAsset(assetId);
  const summary = useFailureSummary(assetId);
  const data = asset.data;
  const history = data?.serviceHistory ?? [];

  return (
    <SlideOver
      open={open}
      onOpenChange={onOpenChange}
      openFullTo={`/assets/${assetId}`}
      title={data ? data.name : asset.isLoading ? 'Loading asset…' : 'Asset'}
      meta={
        data ? (
          <div className={styles.sheetMeta}>
            <TagChip tag={data.assetTag} />
            <StatusPill status={data.status} />
            <WarrantyPill
              isUnderWarranty={summary.data?.isUnderWarranty}
              warrantyExpiresOn={data.warrantyExpiresOn}
              isLoading={summary.isLoading}
              hasError={Boolean(summary.error)}
            />
          </div>
        ) : null
      }
      footer={
        canEdit && data ? (
          <MxButton icon={Pencil} to={`/assets/${assetId}/edit`}>
            Edit asset
          </MxButton>
        ) : null
      }
    >
      {asset.isLoading ? (
        <div className={styles.stack}>
          <Skeleton height={112} radius={16} />
          <Skeleton height={140} radius={16} />
          <Skeleton height={180} radius={16} />
        </div>
      ) : null}

      {!asset.isLoading && asset.error ? (
        <ErrorState
          title={asset.error.status === 404 ? 'Asset not found' : 'Could not load this asset'}
          message={asset.error.status === 404 ? `There is no asset with id ${assetId}.` : asset.error.message}
        />
      ) : null}

      {!asset.isLoading && !asset.error && data ? (
        <div className={styles.stack}>
          <AssetLabel tag={data.assetTag} name={data.name} location={roomLabel(data.room)} size="compact" />

          <dl className={styles.factList}>
            <Fact label="Category" value={data.category.name} />
            <Fact label="Room" value={roomLabel(data.room)} />
            <Fact label="Floor" value={data.room.floor} mono />
            <Fact label="Installed" value={formatDateOnly(data.installedOn)} mono />
            <Fact
              label="Warranty until"
              value={data.warrantyExpiresOn ? formatDateOnly(data.warrantyExpiresOn) : 'Not recorded'}
              mono
            />
          </dl>

          {summary.isLoading ? <Skeleton height={190} radius={20} /> : null}
          {!summary.isLoading && summary.error ? (
            <Panel>
              <ErrorState compact title="Could not load the failure summary" message={summary.error.message} />
            </Panel>
          ) : null}
          {!summary.isLoading && !summary.error && summary.data ? <SummaryCard summary={summary.data} compact /> : null}

          <Panel eyebrow="Latest visit" count={history.length ? `${history.length} on record` : null}>
            {history.length === 0 ? (
              <EmptyState
                compact
                title="No service visits yet"
                body="Visits appear as completed work orders are closed against this asset."
              />
            ) : (
              <>
                {/* The newest visit is the last one the API sent; the full page reads them in order. */}
                <HistoryFeed records={[history[history.length - 1]]} startIndex={history.length - 1} total={history.length} />
                {history.length > 1 ? (
                  <p className={styles.footnote}>Open full to read all {history.length} visits in the order they happened.</p>
                ) : null}
              </>
            )}
          </Panel>
        </div>
      ) : null}
    </SlideOver>
  );
}

function Fact({ label, value, mono = false }) {
  return (
    <div className={styles.fact}>
      <dt>{label}</dt>
      <dd className={mono ? 'mx-mono' : undefined}>{value}</dd>
    </div>
  );
}

export default AssetQuickLook;
