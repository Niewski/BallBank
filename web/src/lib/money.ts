// Amounts are US dollars; the API sends them as numbers with at most two decimals.
export function formatMoney(amount: number): string {
  return `$${Math.abs(amount).toFixed(2)}`;
}

// A balance in league language. Positive means the member owes the pot;
// negative means the pot owes the member, as after an overpayment.
export function describeBalance(balance: number, whose: "you" | "them"): string {
  if (balance > 0) {
    return whose === "you"
      ? `You owe ${formatMoney(balance)}`
      : `Owes ${formatMoney(balance)}`;
  }
  if (balance < 0) {
    return `The pot owes ${whose} ${formatMoney(balance)}`;
  }
  return "Settled";
}
