import { useNavigate } from 'react-router-dom';

import useRoutePanel from '../../../components/ui/useRoutePanel';
import AssetForm from '../components/AssetForm';
import AssetFormSheet from '../components/AssetFormSheet';
import useAssetLookups from '../hooks/useAssetLookups';
import { createAsset } from '../services/assetsApi';
import { EMPTY_ASSET_VALUES } from '../services/assetValidation';

/**
 * Register a new asset — a slide-over on top of the catalogue. Admin only; the route guard
 * refuses every other role. On success it goes to the new asset's page, as before.
 */
export function AssetCreatePage() {
  const navigate = useNavigate();
  const panel = useRoutePanel('/assets');
  const lookups = useAssetLookups();

  async function handleSubmit(values) {
    const created = await createAsset(values);
    navigate(`/assets/${created.id}`);
  }

  return (
    <AssetFormSheet
      panel={panel}
      title="Register asset"
      description="A new record means a new QR sticker on the equipment. It starts out Active."
      isLoading={lookups.isLoading}
      error={lookups.error}
      errorTitle="Could not load categories and rooms"
      renderForm={({ onDirtyChange, onCancel }) => (
        <AssetForm
          mode="create"
          initialValues={EMPTY_ASSET_VALUES}
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

export default AssetCreatePage;
