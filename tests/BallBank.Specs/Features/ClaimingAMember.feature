Feature: Claiming a member
  Whoever opens an invite becomes the member it was issued for, and nobody picks a team from a list.
  An invite can be used for 14 days, and only while it is the latest one for its member.

  Background:
    Given the Sleeper league "Holland Hogs" for the 2026 season with these teams:
      | roster | owner | team name      | commissioner |
      | 1      | Jacob | Hog Wild       | yes          |
      | 2      | Sam   | Sam's Slammers | no           |
      | 3      | Priya |                | no           |
    And Jacob has imported the league
    And Jacob has invited "Priya"

  Scenario: An invited person claims the member through the invite
    When Priya claims "Priya" with the invite
    Then Priya holds the member "Priya"

  Scenario: An invite that has expired is refused
    When Priya claims "Priya" with the invite 14 days later
    Then the claim is refused because the invite has expired
    And "Priya" is unclaimed

  Scenario: An invite a newer one replaced is refused
    Given Jacob has invited "Priya" again
    When Priya claims "Priya" with the first invite
    Then the claim is refused because the invite was replaced
    And "Priya" is unclaimed

  Scenario: Claiming twice claims once
    When Priya claims "Priya" with the invite
    And Priya claims "Priya" with the invite again
    Then Priya holds the member "Priya"
    And "Priya" was claimed once
