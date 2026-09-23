import { Link, useNavigate } from 'react-router-dom';

import ErrorMessage from '../../../components/ErrorMessage';
import Spinner from '../../../components/Spinner';
import AssetForm from '../components/AssetForm';
import useAssetLookups from '../hooks/useAssetLookups';
import { createAsset } from '../services/assetsApi';
import { EMPTY_ASSET_VALUES } from '../services/assetValidation';

/** Register a new asset. Admin only — the route guard refuses every other role. */
export function AssetCreatePage() {
  const navigate = useNavigate();
  const lookups = useAssetLookups();

  async function handleSubmit(values) {
    const created = await createAsset(values);
    navigate(`/assets/${created.id}`);
  }

  return (
    <section className="page page--form">
      <p className="page__back">
        <Link to="/assets">← All assets</Link>
      </p>

      <header className="page-header page-header--stacked">
        <p className="page-header__eyebrow">Asset registry</p>
        <h1>Register asset</h1>
        <p className="page__lead">
          A new record means a new QR sticker on the equipment. It starts out Active.
        </p>
      </header>

      {/* The form cannot be filled in without its pickers, so their states are the page's. */}
      {lookups.isLoading ? <Spinner label="Loading categories and rooms…" /> : null}

      {!lookups.isLoading && lookups.error ? (
        <ErrorMessage title="Could not load categories and rooms" message={lookups.error.message} />
      ) : null}

      {!lookups.isLoading && !lookups.error ? (
        <AssetForm
          mode="create"
          initialValues={EMPTY_ASSET_VALUES}
          categories={lookups.categories}
          rooms={lookups.rooms}
          onSubmit={handleSubmit}
          onCancel={() => navigate('/assets')}
        />
      ) : null}
    </section>
  );
}

export default AssetCreatePage;
