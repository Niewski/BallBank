Feature: Importing a league
  A treasurer-to-be brings their Sleeper league into BallBank. Every Sleeper team becomes a member,
  and the person importing becomes a treasurer holding their own member.

  Background:
    Given the Sleeper league "Holland Hogs" for the 2026 season with these teams:
      | roster | owner | team name      | commissioner |
      | 1      | Jacob | Hog Wild       | yes          |
      | 2      | Sam   | Sam's Slammers | no           |
      | 3      | Priya |                | no           |
      | 4      |       |                | no           |

  Scenario: The importer becomes a treasurer holding their own member
    When Jacob imports the league
    Then Jacob holds the member "Hog Wild"
    And Jacob is a treasurer

  Scenario: Every team becomes a member, including one nobody owns
    When Jacob imports the league
    Then the league has these members:
      | team name      | owner on Sleeper | claimed | suggested treasurer |
      | Hog Wild       | Jacob            | yes     | yes                 |
      | Sam's Slammers | Sam              | no      | no                  |
      | Priya          | Priya            | no      | no                  |
      | Team 4         |                  | no      | no                  |

  Scenario: Someone who owns no team in the league cannot import it
    When Dana imports the league
    Then the import is refused

  Scenario: One Sleeper league cannot back a second league
    Given Jacob has imported the league
    When Sam imports the same Sleeper league as another league
    Then the import is refused

  Scenario: Importing again with nothing new on Sleeper adds nothing
    Given Jacob has imported the league
    When Jacob imports the league again
    Then no members are added
    And the league has these members:
      | team name      | owner on Sleeper | claimed | suggested treasurer |
      | Hog Wild       | Jacob            | yes     | yes                 |
      | Sam's Slammers | Sam              | no      | no                  |
      | Priya          | Priya            | no      | no                  |
      | Team 4         |                  | no      | no                  |

  Scenario: Importing again after a team joins on Sleeper adds that team
    Given Jacob has imported the league
    And this team has since joined the Sleeper league:
      | roster | owner | team name      | commissioner |
      | 5      | Dana  | Dana's Dynasty | no           |
    When Jacob imports the league again
    Then the league has these members:
      | team name      | owner on Sleeper | claimed | suggested treasurer |
      | Hog Wild       | Jacob            | yes     | yes                 |
      | Sam's Slammers | Sam              | no      | no                  |
      | Priya          | Priya            | no      | no                  |
      | Team 4         |                  | no      | no                  |
      | Dana's Dynasty | Dana             | no      | no                  |
    And the members from the first import keep their ids
    And Jacob holds the member "Hog Wild"
    And Jacob is a treasurer

  Scenario: Only a treasurer can import again
    Given Jacob has imported the league
    When Sam imports the league again
    Then the import is refused
