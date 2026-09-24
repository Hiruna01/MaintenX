import { Link } from 'react-router-dom';

import WarrantyBadge from '../../assets/components/WarrantyBadge';
import { formatDateOnly } from '../../assets/services/assetsApi';
import { formatMoney } from '../../workorders/services/workOrdersApi';
import { enumLabel } from '../services/verificationApi';

/**
 * Assets with three or more service visits in the 90-day window, ranked by cost as the API
 * ranks them — the list a manager acts on. Every figure is the API's; the rank number is only
 * the row's position.
 *
 * The cost is what is KNOWN to have been spent: visits with no work order behind them (seeded
 * or imported history) carry no cost, and the count of those sits beside the total so a
 * Rs 0 machine is not read as a free one.
 */
export function RepeatFailureTable({ failures }) {
  return (
    <div className="asset-table metrics-table">
      <table>
        <caption className="visually-hidden">Repeat-failure assets, ranked by cost</caption>
        <thead>
          <tr>
            <th scope="col" className="metrics-table__num">#</th>
            <th scope="col">Asset</th>
            <th scope="col" className="metrics-table__num">Visits</th>
            <th scope="col">Last serviced</th>
            <th scope="col" className="metrics-table__num">Total cost</th>
            <th scope="col">Warranty</th>
          </tr>
        </thead>
        <tbody>
          {failures.map((asset, index) => (
            <tr key={asset.assetId}>
              <td className="metrics-table__num">{index + 1}</td>
              <td>
                <Link className="asset-table__name" to={`/assets/${asset.assetId}`}>
                  {asset.name}
                </Link>
                <span className="asset-table__sub">
                  <span className="asset-tag">{asset.assetTag}</span> · {asset.categoryName}
                  {asset.status !== 'Active' ? ` · ${enumLabel(asset.status)}` : ''}
                </span>
              </td>
              <td className="metrics-table__num">{asset.failureCount}</td>
              <td>
                {formatDateOnly(asset.lastServicedOn)}
                <span className="asset-table__sub">{asset.daysSinceLastService} days ago</span>
              </td>
              <td className="metrics-table__num">
                {formatMoney(asset.totalCost)}
                {asset.visitsWithoutCost > 0 ? (
                  <span className="asset-table__sub">
                    {asset.visitsWithoutCost} visit{asset.visitsWithoutCost === 1 ? '' : 's'} with no cost recorded
                  </span>
                ) : null}
              </td>
              <td>
                <WarrantyBadge
                  isUnderWarranty={asset.isUnderWarranty}
                  warrantyExpiresOn={asset.warrantyExpiresOn}
                  isLoading={false}
                  hasError={false}
                />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default RepeatFailureTable;
