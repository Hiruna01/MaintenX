/**
 * The paths the dashboard reads. Every one is an existing GET the other features already use,
 * built by that feature's own service — the dashboard adds no endpoint and computes no rule.
 * Who sees what is still the API's decision, from the token.
 */

import { buildAssetsPath } from '../../assets/services/assetsApi';
import { buildReportsPath } from '../../reports/services/reportsApi';
import { buildMetricsPath, buildVerificationsPath } from '../../verification/services/verificationApi';
import { buildApprovalQueuePath, buildWorkOrdersPath } from '../../workorders/services/workOrdersApi';

export const approvalQueuePath = () => buildApprovalQueuePath(1);
export const metricsPath = () => buildMetricsPath();
export const latestReportsPath = () => buildReportsPath({ pageSize: 5 });
export const latestWorkOrdersPath = () => buildWorkOrdersPath({ pageSize: 5 });
export const awaitingAnswerPath = () =>
  buildVerificationsPath({ status: 'AwaitingReporterResponse', pageSize: 5 });

/** A page of one — the answer we want is `totalCount`, computed by the API. */
export const workOrderCountPath = (status) => buildWorkOrdersPath({ status, pageSize: 1 });
export const assetCountPath = (status) => buildAssetsPath({ status, pageSize: 1 });
