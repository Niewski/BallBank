Feature: Paying dues
  A member says they paid, the treasurer confirms it, and the books reflect it.
  Money moves between people outside BallBank; BallBank records what happened.

  Background:
    Given a league "Holland Hogs" with members Jacob, Sam, Priya
    And season dues of $50 due on 2026-10-01

  Scenario: A member pays and the treasurer confirms
    When Jacob attests a $50 Venmo payment with reference "VN-1234"
    And the treasurer confirms Jacob's payment
    Then Jacob's balance is $0
    And Sam's balance is $50
    And the league pot is $50
    And the history shows the treasurer confirmed Jacob's payment

  Scenario: A payment does not count until the treasurer confirms it
    When Jacob attests a $50 Venmo payment with reference "VN-1234"
    Then Jacob's balance is $50
    And the league pot is $0
    And Jacob has 1 pending payment

  Scenario: The treasurer rejects a payment they cannot find
    When Jacob attests a $50 Zelle payment with reference "ZL-0000"
    And the treasurer rejects Jacob's payment because "No Zelle with that reference arrived"
    Then Jacob's balance is $50
    And Jacob has 0 pending payments

  Scenario: Attesting the same payment twice records it once
    When Jacob attests a $50 Venmo payment with reference "VN-1234"
    And Jacob attests that same payment again
    Then Jacob has 1 pending payment
