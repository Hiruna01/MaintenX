import { Link, useParams } from 'react-router-dom';

import ErrorMessage from '../../../components/ErrorMessage';
import Spinner from '../../../components/Spinner';
import useAuth from '../../auth/hooks/useAuth';
import { ADMIN_ROLES, hasRole } from '../../auth/services/roles';
import AssetStatusBadge from '../components/AssetStatusBadge';
import FailureSummaryPanel from '../components/FailureSummaryPanel';
import ServiceTimeline from '../components/ServiceTimeline';
import WarrantyBadge from '../components/WarrantyBadge';
import useAsset from '../hooks/useAsset';
import useFailureSummary from '../hooks/useFailureSummary';
import { formatDateOnly, roomLabel } from '../services/assetsApi';

/**
 * One asset: its header, its failure summary, and its full service history.
 *
 * Two requests, each with its own three states. The asset is the page — if it fails the
 * page fails. The summary is a panel on it, so a failed summary is an error in the panel
 * while the history, which is the evidence the summary was computed from, still renders.
 */
export function AssetDetailPage() {
  const { id } = useParams();
  const asset = useAsset(id);
  const summary = useFailureSummary(id);

  const { role } = useAuth();
  const isAdmin = hasRole(role, ADMIN_ROLES);

  return (
    <section className="page asset-detail">
      <p className="page__back">
        <Link to="/assets">← All assets</Link>
      </p>

      {/* All three request states are rendered explicitly. A blank screen is a bug. */}
      {asset.isLoading ? <Spinner label="Loading asset…" /> : null}

      {!asset.isLoading && asset.error ? (
        <ErrorMessage
          title={asset.error.status === 404 ? 'Asset not found' : 'Could not load this asset'}
          message={
            asset.error.status === 404
              ? `There is no asset with id ${id} in the registry.`
              : asset.error.message
          }
        />
      ) : null}

      {!asset.isLoading && !asset.error && asset.data ? (
        <>
          <header className="asset-hero">
            <div className="asset-hero__main">
              <div className="asset-hero__tags">
                <span className="asset-tag asset-tag--large">{asset.data.assetTag}</span>
                <AssetStatusBadge status={asset.data.status} />
                <WarrantyBadge
                  isUnderWarranty={summary.data?.isUnderWarranty}
                  warrantyExpiresOn={asset.data.warrantyExpiresOn}
                  isLoading={summary.isLoading}
                  hasError={Boolean(summary.error)}
                />
              </div>
              <h1>{asset.data.name}</h1>
              <p className="asset-hero__subtitle">
                {asset.data.category.name}
                {asset.data.manufacturer || asset.data.model
                  ? ` · ${[asset.data.manufacturer, asset.data.model].filter(Boolean).join(' ')}`
                  : ''}
              </p>
            </div>

            {isAdmin ? (
              <Link className="button button--secondary" to={`/assets/${asset.data.id}/edit`}>
                Edit asset
              </Link>
            ) : null}

            <dl className="asset-hero__facts">
              <div>
                <dt>Room</dt>
                <dd>{roomLabel(asset.data.room)}</dd>
              </div>
              <div>
                <dt>Floor</dt>
                <dd>{asset.data.room.floor}</dd>
              </div>
              <div>
                <dt>Installed</dt>
                <dd>{formatDateOnly(asset.data.installedOn)}</dd>
              </div>
              <div>
                <dt>Warranty until</dt>
                <dd>
                  {asset.data.warrantyExpiresOn
                    ? formatDateOnly(asset.data.warrantyExpiresOn)
                    : 'Not recorded'}
                </dd>
              </div>
            </dl>
          </header>

          {summary.isLoading ? <Spinner label="Loading failure summary…" /> : null}

          {!summary.isLoading && summary.error ? (
            <ErrorMessage title="Could not load the failure summary" message={summary.error.message} />
          ) : null}

          {!summary.isLoading && !summary.error && summary.data ? (
            <FailureSummaryPanel summary={summary.data} />
          ) : null}

          <section className="history" aria-labelledby="history-heading">
            <header className="history__head">
              <h2 id="history-heading">Service history</h2>
              <p className="history__lead">
                Oldest first, with each technician&apos;s note exactly as written. A repeat
                failure only reads as one in the order it happened.
              </p>
            </header>

            {/* An empty history is not a failure and must not look like one. */}
            {asset.data.serviceHistory.length === 0 ? (
              <div className="empty-state">
                <p className="empty-state__title">No service visits on record</p>
                <p className="empty-state__body">
                  Visits appear here as completed work orders are closed against this asset.
                </p>
              </div>
            ) : (
              <ServiceTimeline records={asset.data.serviceHistory} />
            )}
          </section>
        </>
      ) : null}
    </section>
  );
}

export default AssetDetailPage;
