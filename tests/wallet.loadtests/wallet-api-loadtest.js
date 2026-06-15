import http from 'k6/http';
import { check, group, sleep } from 'k6';

const BASE_URL = __ENV.API_BASE_URL || 'http://localhost:5000/api';
const API_TOKEN = __ENV.API_TOKEN || 'test-token';
const LOOKUP_ALIAS = __ENV.LOOKUP_ALIAS || 'john.doe';
const RECEIVER_WALLET_ID = __ENV.RECEIVER_WALLET_ID || '00000000-0000-0000-0000-000000000000';
const SOURCE_CURRENCY = __ENV.SOURCE_CURRENCY || 'USD';
const DESTINATION_CURRENCY = __ENV.DESTINATION_CURRENCY || 'EUR';
const CURRENCY_CODE = __ENV.CURRENCY_CODE || 'EUR';

export const options = {
  stages: [
    { duration: '20s', target: 10 },
    { duration: '40s', target: 30 },
    { duration: '20s', target: 0 }
  ],
  thresholds: {
    http_req_duration: ['p(95)<500', 'avg<250'],
    http_req_failed: ['rate<0.01']
  }
};

function authHeaders() {
  return {
    Authorization: `Bearer ${API_TOKEN}`,
    'Content-Type': 'application/json'
  };
}

export default function () {
  group('wallet summary', () => {
    const response = http.get(`${BASE_URL}/wallet/summary`, { headers: authHeaders() });
    check(response, {
      'summary status is 200': (r) => r.status === 200
    });
  });

  group('lookup recipient', () => {
    const response = http.get(`${BASE_URL}/wallet/lookup/${encodeURIComponent(LOOKUP_ALIAS)}`, { headers: authHeaders() });
    check(response, {
      'lookup status is 200 or 404': (r) => r.status === 200 || r.status === 404
    });
  });

  group('supported currencies', () => {
    const response = http.get(`${BASE_URL}/wallet/supported-currencies`, { headers: authHeaders() });
    check(response, {
      'currencies status is 200': (r) => r.status === 200
    });
  });

  group('fx quote', () => {
    const payload = JSON.stringify({
      SourceCurrency: SOURCE_CURRENCY,
      ReceivingAmount: 100,
      DestinationCurrency: DESTINATION_CURRENCY
    });

    const response = http.post(`${BASE_URL}/wallet/fx/quote`, payload, { headers: authHeaders() });
    check(response, {
      'fx quote status is 200 or 400': (r) => r.status === 200 || r.status === 400
    });
  });

  group('update wallet status', () => {
    const payload = JSON.stringify({ CurrencyCode: CURRENCY_CODE });
    const response = http.put(`${BASE_URL}/wallet/update-status`, payload, { headers: authHeaders() });
    check(response, {
      'update status is 204 or 400': (r) => r.status === 204 || r.status === 400
    });
  });

  group('send money', () => {
    const payload = JSON.stringify({
      ReceiverWalletId: RECEIVER_WALLET_ID,
      SourceAmount: 10,
      DestinationCurrency: DESTINATION_CURRENCY,
      DestinationAmount: 8.5,
      FxRate: 0.85,
      FeeCurrency: DESTINATION_CURRENCY,
      TransactionFee: 0.25
    });
    const idempotencyKey = `${__VU}-${__ITER__}-${Date.now()}`;
    const response = http.post(
      `${BASE_URL}/wallet/send`,
      payload,
      { headers: { ...authHeaders(), 'Idempotency-Key': idempotencyKey } }
    );

    check(response, {
      'send status is 200 or 400': (r) => r.status === 200 || r.status === 400
    });
  });

  group('list transactions', () => {
    const response = http.get(`${BASE_URL}/wallet/transactions?pageIndex=1&pageSize=10`, { headers: authHeaders() });
    check(response, {
      'transactions status is 200': (r) => r.status === 200
    });
  });

  sleep(1);
}
