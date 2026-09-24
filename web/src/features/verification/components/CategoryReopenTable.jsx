import { formatPercent } from '../services/verificationApi';

/**
 * The reopen rate per asset category, worst first as the API ranks it. Each rate sits beside
 * the counts it was divided from, so 1 of 1 is not mistaken for 40 of 40.
 */
export function CategoryReopenTable({ categories }) {
  return (
    <div className="asset-table metrics-table">
      <table>
        <caption className="visually-hidden">Reopen rate by asset category</caption>
        <thead>
          <tr>
            <th scope="col">Category</th>
            <th scope="col" className="metrics-table__num">Answered</th>
            <th scope="col" className="metrics-table__num">Reopened</th>
            <th scope="col" className="metrics-table__num">Reopen rate</th>
          </tr>
        </thead>
        <tbody>
          {categories.map((category) => (
            <tr key={category.assetCategoryId}>
              <td>{category.categoryName}</td>
              <td className="metrics-table__num">{category.answered}</td>
              <td className="metrics-table__num">{category.reopened}</td>
              <td className="metrics-table__num">{formatPercent(category.reopenRate)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default CategoryReopenTable;
