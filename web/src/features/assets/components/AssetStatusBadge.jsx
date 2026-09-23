import { enumLabel } from '../services/assetsApi';

/**
 * An asset's status as a pill. The modifier class is the enum NAME the API sent
 * ("UnderMaintenance"), never an ordinal, so the colour cannot drift from the meaning.
 */
export function AssetStatusBadge({ status }) {
  return (
    <span className={`asset-status asset-status--${status}`}>
      <span className="asset-status__dot" aria-hidden="true" />
      {enumLabel(status)}
    </span>
  );
}

export default AssetStatusBadge;
