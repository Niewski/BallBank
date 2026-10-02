// Warm latency of confirming a payment: POST /leagues/{league}/accounts/{account}/attestations/{id}/confirmation
// at a steady rate, for the "Warm p50 / p95" row of docs/numbers.md.
//
// Run by hand against a deployed environment, never in CI. It WRITES: every iteration a treasurer attests a $1
// payment on one account of the season and confirms it, so a run adds two events and $1 to the pot per
// iteration (600 iterations, 1,200 events and $600 at the defaults). Point it at a scratch league: one whose
// Discord is not connected and whose members have not opted in to texts, or every confirmation announces itself.
// The token and the league id stay on the command line, never in the repo. The token must belong to a
// treasurer of the league (a treasurer may attest on any account), and the league needs at least 10 accounts
// in the season, because each virtual user keeps to an account of its own and a stale version would be a 409.
//
//   k6 run -e BASE_URL=https://<api host> -e TOKEN=<treasurer's bearer token> \
//          -e LEAGUE=<league id> k6/confirm-payment.js
//
// Optional: -e SEASON=2026 (the season's label), -e RPS=5 (confirmations a second), -e DURATION=2m,
// -e VUS=10 (accounts used at once). The report line to read is http_req_duration{name:confirm}: med is p50.
//
// The setup call reads the ledger first, which wakes the API and its database, so the run measures a warm
// API. The cold start is a different row, measured by hand once the API has been idle.
import http from "k6/http";
import { check } from "k6";

const season = __ENV.SEASON || "2026";
const vus = Number(__ENV.VUS || 10);

export const options = {
  scenarios: {
    confirmations: {
      executor: "constant-arrival-rate",
      rate: Number(__ENV.RPS || 5),
      timeUnit: "1s",
      duration: __ENV.DURATION || "2m",
      preAllocatedVUs: vus,
      maxVUs: vus,
    },
  },
  summaryTrendStats: ["avg", "min", "med", "p(90)", "p(95)", "max"],
  thresholds: {
    // No budget is claimed here: these only make k6 report the confirmation request on a line of its own.
    "http_req_duration{name:confirm}": ["p(50)>=0", "p(95)>=0"],
    http_req_failed: ["rate==0"],
    checks: ["rate==1"],
  },
};

export function setup() {
  for (const name of ["BASE_URL", "TOKEN", "LEAGUE"]) {
    if (!__ENV[name]) throw new Error(`Set ${name}; see the top of this file.`);
  }

  const accounts = ledger();
  if (accounts.length < vus) {
    throw new Error(`Season ${season} has ${accounts.length} accounts and ${vus} are needed (set VUS lower).`);
  }

  return { accounts: accounts.map((account) => account.accountId) };
}

// A virtual user's own copy: the version of its account that it last saw.
let version;

export default function (data) {
  const accountId = data.accounts[(__VU - 1) % data.accounts.length];
  const account = `${__ENV.BASE_URL}/leagues/${__ENV.LEAGUE}/accounts/${accountId}`;

  if (version === undefined) {
    version = ledger().find((entry) => entry.accountId === accountId).version;
  }

  const attestationId = uuid();
  const attested = http.post(
    `${account}/attestations`,
    JSON.stringify({ attestationId, amount: 1, rail: "Cash", reference: null, version }),
    { headers: headers(), tags: { name: "attest" } },
  );
  if (!check(attested, { "the attestation is recorded": (r) => r.status === 201 })) {
    version = undefined;
    return;
  }

  const confirmed = http.post(
    `${account}/attestations/${attestationId}/confirmation`,
    JSON.stringify({ version: attested.json("version") }),
    { headers: headers(), tags: { name: "confirm" } },
  );
  check(confirmed, { "the confirmation is accepted": (r) => r.status === 200 });
  version = confirmed.status === 200 ? confirmed.json("version") : undefined;
}

function ledger() {
  const response = http.get(`${__ENV.BASE_URL}/leagues/${__ENV.LEAGUE}/seasons/${season}/ledger`, {
    headers: { Authorization: `Bearer ${__ENV.TOKEN}` },
    tags: { name: "ledger" },
  });
  if (response.status !== 200) throw new Error(`The ledger answered ${response.status}: ${response.body}`);
  return response.json();
}

function headers() {
  return {
    Authorization: `Bearer ${__ENV.TOKEN}`,
    "Content-Type": "application/json",
    "Idempotency-Key": uuid(),
  };
}

function uuid() {
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = Math.floor(Math.random() * 16);
    return (c === "x" ? r : (r & 0x3) | 0x8).toString(16);
  });
}
