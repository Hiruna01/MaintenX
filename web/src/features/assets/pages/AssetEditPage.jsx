import { Link, useNavigate, useParams } from 'react-router-dom';

import ErrorMessage from '../../../components/ErrorMessage';
import Spinner from '../../../components/Spinner';
import AssetForm from '../components/AssetForm';
import useAsset from '../hooks/useAsset';
import useAssetLookups from '../hooks/useAssetLookups';
import { updateAsset } from '../services/assetsApi';

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

/** Edit an asset. Admin only — the route guard refuses every other role. */
export function AssetEditPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const asset = useAsset(id);
  const lookups = useAssetLookups();

  async function handleSubmit(values) {
    await updateAsset(id, values);
    navigate(`/assets/${id}`);
  }

  const isLoading = asset.isLoading || lookups.isLoading;
  const error = asset.error ?? lookups.error;

  return (
    <section className="page page--form">
      <p className="page__back">
        <Link to={`/assets/${id}`}>← Back to the asset</Link>
      </p>

      <header className="page-header page-header--stacked">
        <p className="page-header__eyebrow">Asset registry</p>
        <h1>{asset.data ? `Edit ${asset.data.name}` : 'Edit asset'}</h1>
      </header>

      {/* All three request states are rendered explicitly. A blank screen is a bug. */}
      {isLoading ? <Spinner label="Loading asset…" /> : null}

      {!isLoading && error ? (
        <ErrorMessage
          title={error.status === 404 ? 'Asset not found' : 'Could not load this asset'}
          message={
            error.status === 404 ? `There is no asset with id ${id} in the registry.` : error.message
          }
        />
      ) : null}

      {!isLoading && !error && asset.data ? (
        <AssetForm
          mode="edit"
          initialValues={toFormValues(asset.data)}
          categories={lookups.categories}
          rooms={lookups.rooms}
          onSubmit={handleSubmit}
          onCancel={() => navigate(`/assets/${id}`)}
        />
      ) : null}
    </section>
  );
}

export default AssetEditPage;
