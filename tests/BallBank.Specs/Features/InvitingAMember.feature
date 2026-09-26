Feature: Inviting a member
  A treasurer issues an invite for one member, so whoever opens it becomes exactly that member and
  nobody has to pick a member from a list. The treasurer sends it themselves; BallBank sends nothing.

  Background:
    Given the Sleeper league "Holland Hogs" for the 2026 season with these teams:
      | roster | owner | team name      | commissioner |
      | 1      | Jacob | Hog Wild       | yes          |
      | 2      | Sam   | Sam's Slammers | no           |
      | 3      | Priya |                | no           |
    And Jacob has imported the league
    And Sam holds the member "Sam's Slammers"

  Scenario: A treasurer invites a member who has not claimed yet
    When Jacob invites "Priya"
    Then the invite for "Priya" can be used for 14 days

  Scenario: A new invite voids the earlier one
    Given Jacob has invited "Priya"
    When Jacob invites "Priya" again
    Then only the newer invite for "Priya" can be used

  Scenario: Sending the same invite twice issues it once
    Given Jacob has invited "Priya"
    When Jacob sends that same invite again
    Then the invite for "Priya" can be used for 14 days

  Scenario: A member someone already holds cannot be invited
    When Jacob invites "Sam's Slammers"
    Then the invite is refused

  Scenario: A member who is not a treasurer cannot invite anyone
    When Sam invites "Priya"
    Then the invite is refused
