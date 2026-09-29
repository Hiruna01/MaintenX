import { useMemo } from 'react';
import { useParams } from 'react-router-dom';

import { StatusPill } from '../../../components/ui/Pill';
import useRoutePanel from '../../../components/ui/useRoutePanel';
import AssetForm from '../components/AssetForm';
import AssetFormSheet from '../components/AssetFormSheet';
import TagChip from '../components/TagChip';
import useAsset from '../hooks/useAsset';
import useAssetLookups from '../hooks/useAssetLookups';
import { updateAsset } from '../services/assetsApi';
import styles from '../assets.module.css';

/** The API's AssetDetailDto, as the form's string-valued fields. */
function toFormValues(asset) {
  return {
    assetTag: asset.assetTag,
    name: asset.name,
    assetCategoryId: String(asset.category.id),
    roomId: String(asset.room.id),
    manufacturer: asset.manufacturer ?? '',
    model: asset.model ?? '',
    installedOn: asset.installedOn,
    warrantyExpiresOn: asset.warrantyExpiresOn ?? '',
    status: asset.status,
  };
}

/**
 * Edit an asset — a slide-over on top of its detail page. Admin only; the route guard
 * refuses every other role. Saving slides the panel away and tells the detail page to
 * reload, so the change shows at once.
 */
export function AssetEditPage() {
  const { id } = useParams();
  const panel = useRoutePanel(`/assets/${id}`);
  const asset = useAsset(id);
  const lookups = useAssetLookups();

  const initialValues = useMemo(() => (asset.data ? toFormValues(asset.data) : null), [asset.data]);

  async function handleSubmit(values) {
    await updateAsset(id, values);
    panel.closeThen(`/assets/${id}`, { state: { refresh: Date.now() } });
  }

  const error = asset.error ?? lookups.error;

  return (
    <AssetFormSheet
      panel={panel}
      title={asset.data ? asset.data.name : 'Edit asset'}
      description={asset.data ? 'Edit the record. The tag stays as printed.' : undefined}
      meta={
        asset.data ? (
          <div className={styles.sheetMeta}>
            <TagChip tag={asset.data.assetTag} />
            <StatusPill status={asset.data.status} />
          </div>
        ) : null
      }
      isLoading={asset.isLoading || lookups.isLoading}
      error={error}
      errorTitle={error?.status === 404 ? 'Asset not found' : 'Could not load this asset'}
      renderForm={({ onDirtyChange, onCancel }) => (
        <AssetForm
          mode="edit"
          initialValues={initialValues}
          categories={lookups.categories}
          rooms={lookups.rooms}
          onSubmit={handleSubmit}
          onCancel={onCancel}
          onDirtyChange={onDirtyChange}
        />
      )}
    />
  );
}

export default AssetEditPage;
