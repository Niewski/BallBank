Feature: Appointing a treasurer
  A treasurer appoints another member who has claimed their team, so someone else can keep the books
  while they are away. There is one Treasurer role: a second appointee is simply another treasurer.
  Sleeper's commissioners are only a suggestion of whom to appoint.

  Background:
    Given the Sleeper league "Holland Hogs" for the 2026 season with these teams:
      | roster | owner | team name      | commissioner |
      | 1      | Jacob | Hog Wild       | yes          |
      | 2      | Sam   | Sam's Slammers | yes          |
      | 3      | Priya |                | no           |
    And Jacob has imported the league
    And Sam holds the member "Sam's Slammers"

  Scenario: A treasurer appoints a suggested treasurer
    When Jacob appoints "Sam's Slammers" as a treasurer
    Then Sam is a treasurer
    And Jacob is a treasurer
    And the history shows Jacob appointed Sam

  Scenario: An appointed treasurer can act as one
    Given Jacob has appointed "Sam's Slammers" as a treasurer
    When Sam invites "Priya"
    Then the invite for "Priya" can be used for 14 days

  Scenario: A member nobody has claimed cannot be appointed
    When Jacob appoints "Priya" as a treasurer
    Then the appointment is refused

  Scenario: Appointing twice appoints once
    When Jacob appoints "Sam's Slammers" as a treasurer
    And Jacob appoints "Sam's Slammers" as a treasurer again
    Then Sam is a treasurer
    And "Sam's Slammers" was appointed once

  Scenario: A member who is not a treasurer cannot appoint anyone
    Given Priya holds the member "Priya"
    When Sam appoints "Priya" as a treasurer
    Then the appointment is refused
    And Priya is not a treasurer
