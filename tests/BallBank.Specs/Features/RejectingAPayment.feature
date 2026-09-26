Feature: Rejecting a payment
  The treasurer turns down a payment they cannot find, and says why. The member sees the reason and
  can attest the payment again. Once a payment is confirmed it stays confirmed; a mistake is fixed
  with an adjustment.

  Background:
    Given a league "Holland Hogs" with members Jacob, Sam
    And season dues of $50 due on 2026-10-01

  Scenario: A rejection needs a reason
    When Jacob attests a $50 Zelle payment with reference "ZL-0000"
    And the treasurer rejects Jacob's payment without a reason
    Then the payment is refused because "Rejecting a payment needs a reason the member will see."
    And Jacob has 1 pending payment

  Scenario: A rejected payment can be attested again
    When Jacob attests a $50 Zelle payment with reference "ZL-0000"
    And the treasurer rejects Jacob's payment because "No Zelle with that reference arrived"
    And Jacob attests a $50 Zelle payment with reference "ZL-1234"
    Then Jacob has 1 pending payment
    When the treasurer confirms Jacob's payment
    Then Jacob's balance is $0

  Scenario: A confirmed payment cannot be rejected
    When Jacob attests a $50 Venmo payment with reference "VN-1234"
    And the treasurer confirms Jacob's payment
    And the treasurer rejects Jacob's payment because "Wrong account"
    Then the payment is refused because "A confirmed payment cannot be rejected; post an adjustment instead."
    And Jacob's balance is $0
