Feature: Texting members
  A member chooses to be texted, at their own number, and when not to be. Consent is theirs alone:
  a treasurer cannot give it for them, and it belongs to the number, so a new number is not texted
  until the member opts in at it. Opted in, a member is texted what happens on their own account (dues
  assessed, a payment confirmed or rejected, an adjustment), and a treasurer is texted when a member says
  they paid. Nobody is texted about what they did themselves, nor inside their quiet hours; those texts
  wait until the hours end.

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

  Scenario: Opening a season texts each opted-in member their dues, and nobody else
    Given Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    And it is outside Sam's quiet hours
    When the treasurer opens the "2026" season with dues of $50 due on 2026-10-01
    Then Sam was texted:
      | message                                                                                                        |
      | BallBank: Holland Hogs assessed you $50.00 for Season dues, due Oct 1, 2026. Your statement: {Sam's statement} |
    And Priya was not texted

  Scenario: An assessment texts the member what it is for and when it is due
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    And it is outside Sam's quiet hours
    When the treasurer assesses Sam $25 for "Trophy fund" due on 2026-11-01
    Then Sam was texted:
      | message                                                                                                        |
      | BallBank: Holland Hogs assessed you $25.00 for Trophy fund, due Nov 1, 2026. Your statement: {Sam's statement} |

  Scenario: A treasurer is texted when a member says they paid
    Given season dues of $50 due on 2026-10-01
    And Jacob has recorded the phone number "555 010 0001"
    And Jacob has opted in to texts
    And it is outside Jacob's quiet hours
    When Sam attests a $50 Venmo payment with reference "VN-1234"
    Then Jacob was texted:
      | message                                                                                                         |
      | BallBank: Sam says they paid $50.00 by Venmo (VN-1234) in Holland Hogs. Confirm or reject it: {Sam's statement} |
    And Sam was not texted

  Scenario: A member is texted when the treasurer confirms their payment
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    And it is outside Sam's quiet hours
    When Sam attests a $50 Venmo payment with reference "VN-1234"
    And the treasurer confirms Sam's payment
    Then Sam was texted:
      | message                                                                                       |
      | BallBank: Holland Hogs confirmed your $50.00 Venmo payment. Your statement: {Sam's statement} |

  Scenario: A rejection carries its reason
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    And it is outside Sam's quiet hours
    When Sam attests a $50 Zelle payment with reference "ZL-0000"
    And the treasurer rejects Sam's payment because "No Zelle with that reference arrived"
    Then Sam was texted:
      | message                                                                                                                            |
      | BallBank: Holland Hogs rejected your $50.00 Zelle payment: No Zelle with that reference arrived. Your statement: {Sam's statement} |

  Scenario: An adjustment carries its reason
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    And it is outside Sam's quiet hours
    When the treasurer adjusts Sam's balance by -$5 because "Waived: hosted the draft"
    Then Sam was texted:
      | message                                                                                                           |
      | BallBank: Holland Hogs lowered your balance by $5.00: Waived: hosted the draft. Your statement: {Sam's statement} |

  Scenario: A treasurer is not texted about what they did themselves
    Given season dues of $50 due on 2026-10-01
    And Jacob has recorded the phone number "555 010 0001"
    And Jacob has opted in to texts
    And it is outside Jacob's quiet hours
    When Jacob attests a $30 Cash payment
    And the treasurer confirms Jacob's payment
    And the treasurer adjusts Jacob's balance by -$5 because "Waived: hosted the draft"
    Then Jacob was not texted

  Scenario: A member who did not opt in is not texted
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0002"
    And it is outside Sam's quiet hours
    When Sam attests a $50 Zelle payment with reference "ZL-0000"
    And the treasurer rejects Sam's payment because "No Zelle with that reference arrived"
    Then Sam was not texted

  Scenario: A member who opts out is no longer texted
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    And it is outside Sam's quiet hours
    When Sam attests a $50 Zelle payment with reference "ZL-0000"
    And Sam opts out of texts
    And the treasurer rejects Sam's payment because "No Zelle with that reference arrived"
    Then Sam was not texted

  Scenario: A member is not texted inside their quiet hours
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    And it is inside Sam's quiet hours
    When Sam attests a $50 Zelle payment with reference "ZL-0000"
    And the treasurer rejects Sam's payment because "No Zelle with that reference arrived"
    Then Sam was not texted

  Scenario: A text held for a member's quiet hours arrives when they end
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    And it is inside Sam's quiet hours
    When Sam attests a $50 Venmo payment with reference "VN-1234"
    And the treasurer confirms Sam's payment
    And the tick runs 1 hour later
    Then Sam was not texted
    When the tick runs 3 hours later
    Then Sam was texted:
      | message                                                                                       |
      | BallBank: Holland Hogs confirmed your $50.00 Venmo payment. Your statement: {Sam's statement} |

  Scenario: Confirming a payment twice texts the member once
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0002"
    And Sam has opted in to texts
    And it is outside Sam's quiet hours
    When Sam attests a $50 Venmo payment with reference "VN-1234"
    And the treasurer confirms Sam's payment
    And the treasurer confirms Sam's payment
    Then Sam was texted:
      | message                                                                                       |
      | BallBank: Holland Hogs confirmed your $50.00 Venmo payment. Your statement: {Sam's statement} |

  # Twilio calls back as the texts go and as members reply, so these run over HTTP, where a call can be signed.

  @http
  Scenario Outline: The treasurer can tell a text that arrived from one that did not, and from one still on its way
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0050"
    And Sam has opted in to texts
    And it is outside Sam's quiet hours
    When the treasurer assesses Sam $25 for "Trophy fund" due on 2026-11-01
    Then Sam's text shows as sent
    When Twilio reports Sam's text as "<reported>"
    Then Sam's text shows as <shown>

    Examples:
      | reported    | shown       |
      | queued      | sent        |
      | sent        | sent        |
      | delivered   | delivered   |
      | undelivered | undelivered |
      | failed      | failed      |

  @http
  Scenario: A report made twice records one status
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0050"
    And Sam has opted in to texts
    And it is outside Sam's quiet hours
    When the treasurer assesses Sam $25 for "Trophy fund" due on 2026-11-01
    And Twilio reports Sam's text as "delivered"
    And Twilio reports Sam's text as "delivered"
    Then Twilio is answered that all is well
    And Sam's text shows as delivered

  @http
  Scenario: A report that arrives late does not take a status back
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0050"
    And Sam has opted in to texts
    And it is outside Sam's quiet hours
    When the treasurer assesses Sam $25 for "Trophy fund" due on 2026-11-01
    And Twilio reports Sam's text as "delivered"
    And Twilio reports Sam's text as "sent"
    Then Sam's text shows as delivered

  @http
  Scenario: A report that Twilio did not sign is refused and changes nothing
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0050"
    And Sam has opted in to texts
    And it is outside Sam's quiet hours
    When the treasurer assesses Sam $25 for "Trophy fund" due on 2026-11-01
    And someone who is not Twilio reports Sam's text as "delivered"
    Then the request is refused
    And Sam's text shows as sent

  @http
  Scenario: STOP stops a number's texts in every league it is in, and START resumes them
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0052"
    And Sam has opted in to texts
    And it is outside Sam's quiet hours
    And Sam is also in a second league at that number, opted in and outside their quiet hours
    When Sam replies "STOP"
    Then Sam is opted out of texts
    When the treasurer assesses Sam $25 for "Trophy fund" due on 2026-11-01
    And the second league's treasurer assesses Sam $25 for "Trophy fund" due on 2026-11-01
    Then Sam was not texted
    And Sam was not texted in the second league
    When Sam replies "START"
    Then Sam is not opted out of texts
    When the treasurer assesses Sam $10 for "Banquet" due on 2026-12-01
    And the second league's treasurer assesses Sam $10 for "Banquet" due on 2026-12-01
    Then Sam was texted:
      | message                                                                                                       |
      | BallBank: Holland Hogs assessed you $10.00 for Banquet, due Dec 1, 2026. Your statement: {Sam's statement} |
    And Sam was texted once in the second league

  @http
  Scenario: Any other reply leaves a number's texts as they were
    Given season dues of $50 due on 2026-10-01
    And Sam has recorded the phone number "555 010 0053"
    And Sam has opted in to texts
    And it is outside Sam's quiet hours
    When Sam replies "Thanks!"
    And the treasurer assesses Sam $25 for "Trophy fund" due on 2026-11-01
    Then Twilio is answered that all is well
    And Sam is not opted out of texts
    And Sam was texted:
      | message                                                                                                        |
      | BallBank: Holland Hogs assessed you $25.00 for Trophy fund, due Nov 1, 2026. Your statement: {Sam's statement} |

  @http
  Scenario: A STOP that Twilio did not sign stops nobody's texts
    Given Sam has recorded the phone number "555 010 0054"
    And Sam has opted in to texts
    When someone who is not Twilio sends "STOP" from Sam's number
    Then the request is refused
    And Sam is not opted out of texts
