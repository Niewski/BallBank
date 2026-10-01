// Rate-limit proof (ADR-0013): one league is driven past its request budget and is refused with 429,
// while a second league, called at the same moment, is answered every time.
//
// Run by hand against a deployed environment, never in CI: it is meant to be refused. The token and the
// league ids stay on the command line, never in the repo. The token must belong to a member of both leagues.
//
//   k6 run -e BASE_URL=https://<api host> -e TOKEN=<bearer token> \
//          -e NOISY_LEAGUE=<league id> -e QUIET_LEAGUE=<league id> k6/rate-limit.js
//
// Optional: -e NOISY_RPS=300 (requests a second to the noisy league; the default budget is a burst of 200
// and 50 a second) and -e DURATION=30s.
import http from "k6/http";
import { check } from "k6";
import { Counter } from "k6/metrics";

const duration = __ENV.DURATION || "30s";

const noisyAnswered = new Counter("noisy_answered");
const noisyRefused = new Counter("noisy_refused");
const quietAnswered = new Counter("quiet_answered");
const quietRefused = new Counter("quiet_refused");

// A 429 is the expected answer to the noisy league, not a failed request.
http.setResponseCallback(http.expectedStatuses(200, 429));

export const options = {
  scenarios: {
    noisy: {
      executor: "constant-arrival-rate",
      exec: "noisy",
      rate: Number(__ENV.NOISY_RPS || 300),
      timeUnit: "1s",
      duration,
      preAllocatedVUs: 50,
      maxVUs: 200,
    },
    quiet: {
      executor: "constant-arrival-rate",
      exec: "quiet",
      rate: 2,
      timeUnit: "1s",
      duration,
      preAllocatedVUs: 2,
      maxVUs: 10,
    },
  },
  thresholds: {
    noisy_refused: ["count>0"],
    quiet_answered: ["count>0"],
    quiet_refused: ["count==0"],
    checks: ["rate==1"],
  },
};

export function setup() {
  for (const name of ["BASE_URL", "TOKEN", "NOISY_LEAGUE", "QUIET_LEAGUE"]) {
    if (!__ENV[name]) throw new Error(`Set ${name}; see the top of this file.`);
  }
}

function members(league) {
  return http.get(`${__ENV.BASE_URL}/leagues/${league}/members`, {
    headers: { Authorization: `Bearer ${__ENV.TOKEN}` },
  });
}

export function noisy() {
  const response = members(__ENV.NOISY_LEAGUE);
  if (response.status === 429) {
    noisyRefused.add(1);
    check(response, {
      "a refusal says when to come back": (r) => Number(r.headers["Retry-After"]) > 0,
      "a refusal is problem details": (r) =>
        (r.headers["Content-Type"] || "").includes("application/problem+json"),
    });
  } else {
    noisyAnswered.add(1);
    check(response, { "an answer is a 200": (r) => r.status === 200 });
  }
}

export function quiet() {
  const response = members(__ENV.QUIET_LEAGUE);
  if (response.status === 429) {
    quietRefused.add(1);
  } else {
    quietAnswered.add(1);
  }
  check(response, { "the other league is answered": (r) => r.status === 200 });
}
