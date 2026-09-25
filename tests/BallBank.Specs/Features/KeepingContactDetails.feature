Feature: Keeping contact details
  A treasurer records how to reach each member, claimed or not, so they can chase dues and text an
  invite to someone who has not signed in yet. A member keeps their own. Nobody else changes them.

  Background:
    Given the Sleeper league "Holland Hogs" for the 2026 season with these teams:
      | roster | owner | team name      | commissioner |
      | 1      | Jacob | Hog Wild       | yes          |
      | 2      | Sam   | Sam's Slammers | no           |
      | 3      | Priya |                | no           |
    And Jacob has imported the league
    And Sam holds the member "Sam's Slammers"

  Scenario: A treasurer records contact details for a member who has not claimed yet
    When Jacob records these contact details for "Priya":
      | email             | phone          | discord username |
      | priya@example.com | (555) 010-0003 | @Priya           |
    Then "Priya" has these contact details:
      | email             | phone        | discord username |
      | priya@example.com | +15550100003 | priya            |

  Scenario Outline: Any common way of writing a US number is the same number
    When Jacob records the phone number "<entered>" for "Priya"
    Then "Priya" has the phone number "+15550100000"

    Examples:
      | entered         |
      | (555) 010-0000  |
      | 555.010.0000    |
      | +1 555 010 0000 |

  Scenario: A number outside the US is refused
    When Jacob records the phone number "+44 20 7946 0000" for "Priya"
    Then the contact details are refused
    And "Priya" has no contact details

  Scenario Outline: A Discord username is kept lowercase without the @
    When Jacob records the Discord username "<entered>" for "Priya"
    Then "Priya" has the Discord username "somehandle"

    Examples:
      | entered     |
      | @SomeHandle |
      | somehandle  |

  Scenario: A member keeps their own contact details
    When Sam records these contact details for "Sam's Slammers":
      | email           | phone        | discord username |
      | sam@example.com | 555-010-0002 |                  |
    Then "Sam's Slammers" has these contact details:
      | email           | phone        | discord username |
      | sam@example.com | +15550100002 |                  |

  Scenario: A member cannot change someone else's contact details
    When Sam records the phone number "555 010 0003" for "Priya"
    Then the contact details are refused
    And "Priya" has no contact details

  Scenario: Once a member is claimed, their contact details need an email
    When Jacob records the phone number "555 010 0002" for "Sam's Slammers"
    Then the contact details are refused

  Scenario: Clearing a member's contact details keeps nothing about them
    Given Jacob has recorded the phone number "555 010 0003" for "Priya"
    When Jacob clears the contact details for "Priya"
    Then "Priya" has no contact details
