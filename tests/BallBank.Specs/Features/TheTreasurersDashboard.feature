Feature: The treasurer's dashboard
  A treasurer sees where a season stands without adding up ledgers: how much was assessed, what the
  members have paid in, what has been refunded, what is left in the pot, what members still owe and
  what the pot owes them. Members who still owe after the dues were due are delinquent, most overdue
  first. Only a treasurer sees it.

  Background:
    Given a league "Holland Hogs" with members Jacob, Sam, Priya

  Scenario: The figures after a mixed history
    Given season dues of $50 due on 2026-10-01
    When Sam attests a $60 Cash payment
    And the treasurer confirms Sam's payment
    And the treasurer refunds Sam $4 because "Refunded part of the overpayment"
    And Priya attests a $20 Cash payment
    And the treasurer rejects Priya's payment because "No such payment arrived"
    And Jacob attests a $30 Cash payment
    And the treasurer confirms Jacob's payment
    And the treasurer adjusts Jacob's balance by -$5 because "Waived: hosted the draft"
    Then the dashboard shows
      | Assessed | Confirmed | Refunded | Pot | Outstanding | Owed |
      | $150     | $90       | $4       | $86 | $65         | $6   |

  Scenario: Delinquents are the members who still owe after the dues were due
    Given season dues of $50 due 10 days ago
    When Sam attests a $50 Cash payment
    And the treasurer confirms Sam's payment
    And Jacob attests a $30 Cash payment
    And the treasurer confirms Jacob's payment
    Then the delinquents are
      | Member | Balance | Days overdue |
      | Priya  | $50     | 10           |
      | Jacob  | $20     | 10           |

  Scenario: Nobody is delinquent before the dues are due
    Given season dues of $50 due 5 days from now
    Then the dashboard shows no delinquents

  Scenario: A member cannot read the dashboard
    Given season dues of $50 due on 2026-10-01
    When Sam reads the dashboard
    Then Sam is not allowed to
