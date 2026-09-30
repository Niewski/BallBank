Feature: Texting members
  A member chooses to be texted, at their own number, and when not to be. Consent is theirs alone:
  a treasurer cannot give it for them, and it belongs to the number, so a new number is not texted
  until the member opts in at it. Nothing is sent yet; this is the record that someone asked.

  Background:
    Given a league "Holland Hogs" with members Jacob, Sam, Priya

  Scenario: A member opts in at their own number
    Given Sam has recorded the phone number "555 010 0002"
    When Sam opts in to texts
    Then Sam is opted in to texts at "+15550100002"

  Scenario: A treasurer cannot opt a member in
    Given Sam has recorded the phone number "555 010 0002"
    When Jacob opts Sam in to texts
    Then Jacob is not allowed to
    And Sam is not opted in to texts

  Scenario: A member cannot opt another member in
    Given Priya has recorded the phone number "555 010 0003"
    When Sam opts Priya in to texts
    Then Sam is not allowed to
    And Priya is not opted in to texts

  Scenario: Consent needs a phone number on record
    When Sam opts in to texts
    Then opting in is refused because "Text messages go to a specific number. Add your phone number first."
    And Sam is not opted in to texts

  Scenario: A member withdraws consent
    Given Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    When Sam opts out of texts
    Then Sam is not opted in to texts

  Scenario: A new phone number is not texted until the member opts in at it
    Given Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    When Sam records the phone number "555 010 0009"
    Then Sam is not opted in to texts
    When Sam opts in to texts
    Then Sam is opted in to texts at "+15550100009"

  Scenario: A treasurer changing a member's number takes their consent with it
    Given Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    When Jacob records the phone number "555 010 0009" for Sam
    Then Sam is not opted in to texts

  Scenario: Saving the same number again keeps consent
    Given Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    When Sam records the phone number "(555) 010-0002"
    Then Sam is opted in to texts at "+15550100002"

  Scenario: Quiet hours are nine at night to nine in the morning Eastern until a member chooses others
    Then Sam's quiet hours are 21 to 9 in "America/New_York"

  Scenario: A member keeps their own quiet hours, on their own clock
    When Sam sets their quiet hours from 22 to 7 in "America/Chicago"
    Then Sam's quiet hours are 22 to 7 in "America/Chicago"

  Scenario: Choosing quiet hours does not change whether a member is opted in
    Given Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    When Sam sets their quiet hours from 22 to 7 in "America/Chicago"
    Then Sam is opted in to texts at "+15550100002"
