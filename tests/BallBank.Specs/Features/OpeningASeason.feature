Feature: Opening a season
  A treasurer opens the season from the league page with a label, the dues amount and the due date. In
  one transaction every current member gets an account with the season dues assessed. Opening a season
  that is already open changes nothing, so a retry or a second treasurer's click cannot double-assess.

  Background:
    Given a league with members Jacob, Sam, Priya

  Scenario: Opening a season assesses every current member
    When the treasurer opens the "2026" season with dues of $50 due on 2026-10-01
    Then Jacob owes $50
    And Sam owes $50
    And Priya owes $50

  Scenario: Opening a season twice assesses once
    Given the treasurer has opened the "2026" season with dues of $50 due on 2026-10-01
    When the treasurer opens the "2026" season with dues of $50 due on 2026-10-01 again
    Then Jacob owes $50
    And the season was opened once
