Feature: Adjustments need a reason
  A treasurer corrects a member's balance with an adjustment: a waiver lowers what the member owes, a
  refund of an overpayment settles what the pot owed them, a correction goes either way. Every
  adjustment says why, and the member reads it on their statement. Only a treasurer posts one. Only
  a refund moves money, so only a refund takes anything out of the pot.

  Background:
    Given a league "Holland Hogs" with members Jacob, Sam
    And season dues of $50 due on 2026-10-01

  Scenario: A waiver lowers the balance
    When the treasurer adjusts Sam's balance by -$50 because "Waived: hosted the draft"
    Then Sam's balance is $0
    And Jacob's balance is $50
    And the league pot is $0

  Scenario: A refund of an overpayment
    When Jacob attests a $60 Cash payment
    And the treasurer confirms Jacob's payment
    Then the pot owes Jacob $10
    When the treasurer refunds Jacob $10 because "Refunded the $10 overpaid"
    Then Jacob's balance is $0
    And the league pot is $50

  Scenario: An adjustment without a reason is refused
    When the treasurer adjusts Sam's balance by -$50 without a reason
    Then the adjustment is refused because "An adjustment needs a reason the member will see."
    And Sam's balance is $50

  Scenario: A member cannot post an adjustment
    When Sam adjusts Sam's balance by -$50 because "Short this month"
    Then Sam is not allowed to
    And Sam's balance is $50
