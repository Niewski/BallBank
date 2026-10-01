Feature: Reminding members
  A member who still owes is texted a reminder as their dues come due: three days before the earliest
  due date, on the day, and every whole week they stay overdue. The tick sends them, once an hour; a
  reminder is sent once however often the tick runs. A member who has paid up, or has not opted in to
  texts, is not reminded, and one in their quiet hours is texted when the hours end. The treasurer
  sees how the last reminder to each delinquent member went.

  Background:
    Given a league "Holland Hogs" with members Jacob, Sam, Priya

  Scenario: A member who owes is reminded three days before the dues are due
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    When the tick runs on 2026-09-28
    Then Sam was texted:
      | message                                                                                                         |
      | BallBank: You owe Holland Hogs $50.00, due in 3 days on Oct 1, 2026. Your statement: {Sam's statement} |

  Scenario: A member who owes is reminded on the day the dues are due
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    When the tick runs on 2026-10-01
    Then Sam was texted:
      | message                                                                           |
      | BallBank: You owe Holland Hogs $50.00, due today. Your statement: {Sam's statement} |

  Scenario: A member who stays overdue is reminded every week
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    When the tick runs on 2026-10-01
    And the tick runs on 2026-10-08
    And the tick runs on 2026-10-15
    Then Sam was texted:
      | message                                                                                                       |
      | BallBank: You owe Holland Hogs $50.00, due today. Your statement: {Sam's statement}                           |
      | BallBank: You owe Holland Hogs $50.00, 7 days overdue since Oct 1, 2026. Your statement: {Sam's statement}   |
      | BallBank: You owe Holland Hogs $50.00, 14 days overdue since Oct 1, 2026. Your statement: {Sam's statement}  |

  Scenario: A member is not reminded more than three days ahead
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    When the tick runs on 2026-09-27
    Then Sam was not texted

  Scenario: A reminder is sent once, however often the tick runs
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    When the tick runs on 2026-10-01 at 14:00
    And the tick runs on 2026-10-01 at 14:00
    And the tick runs on 2026-10-01 at 18:00
    Then Sam was texted:
      | message                                                                              |
      | BallBank: You owe Holland Hogs $50.00, due today. Your statement: {Sam's statement} |

  Scenario: A member who has paid up is not reminded
    Given season dues of $50 due on 2026-10-01
    When Sam attests a $50 Venmo payment with reference "VN-1234"
    And the treasurer confirms Sam's payment
    And Sam records the phone number "555 010 0002"
    And Sam opts in to texts
    And the tick runs on 2026-10-01
    Then Sam was not texted

  Scenario: A payment waiting for the treasurer does not stop the reminders
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    When Sam attests a $50 Venmo payment with reference "VN-1234"
    And the tick runs on 2026-10-01
    Then Sam was texted:
      | message                                                                              |
      | BallBank: You owe Holland Hogs $50.00, due today. Your statement: {Sam's statement} |

  Scenario: A member who has not opted in is not reminded
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0002"
    When the tick runs on 2026-10-01
    Then Sam was not texted

  Scenario: A reminder due in a member's quiet hours waits for them to end
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    When the tick runs on 2026-10-01 at 02:00
    Then Sam was not texted
    When the tick runs on 2026-10-01 at 14:00
    Then Sam was texted:
      | message                                                                              |
      | BallBank: You owe Holland Hogs $50.00, due today. Your statement: {Sam's statement} |

  Scenario: The treasurer sees how the last reminder to each delinquent member went
    Given season dues of $50 due 7 days ago
    And Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    And it is outside Sam's quiet hours
    And Priya has recorded the phone number "555 010 0003"
    Then the dashboard shows no reminder for Sam
    When the tick runs now
    Then the dashboard shows Sam's last reminder as Sent
    And the dashboard shows Priya's last reminder as Skipped
