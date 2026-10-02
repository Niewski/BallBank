Feature: Posting the weekly digest
  Once a week BallBank posts a digest to the league's Discord channel, if the treasurer asked for it: who
  still owes and how much, what the pot holds, and how many payments are waiting for the treasurer to
  confirm. The tick posts it, in the first tick at or after Monday 09:00 Eastern for a week not yet told
  of, so a tick that comes late still posts it and one that comes again does not repeat it. A league that
  did not ask for the digest, or has disconnected Discord, is never posted one.

  Background:
    Given a league "Holland Hogs" with members Jacob, Sam, Priya
    And season dues of $50 due on 2026-10-01

  Scenario: The digest says who still owes, what the pot holds and how many payments are waiting
    Given the treasurer has connected Discord, posting the weekly digest
    When Sam attests a $50 Venmo payment with reference "VN-1234"
    And the treasurer confirms Sam's payment
    And Priya attests a $20 Cash payment
    And the tick runs on 2026-09-28 at 13:00
    Then the latest weekly digest in Discord reads:
      | line                                               |
      | Weekly digest for Holland Hogs, 2026 season.       |
      | Still owing: Hog Wild $50.00, Priya $50.00.        |
      | The pot holds $50.00.                              |
      | 1 payment is waiting for the treasurer to confirm. |

  Scenario: The digest is posted by the first tick at or after Monday 09:00 Eastern
    Given the treasurer has connected Discord, posting the weekly digest
    When the tick runs on 2026-09-28 at 12:59
    Then Discord was told no weekly digest
    When the tick runs on 2026-09-28 at 13:00
    Then Discord was told 1 weekly digest

  Scenario: A tick that comes late in the week still posts the digest
    Given the treasurer has connected Discord, posting the weekly digest
    When the tick runs on 2026-09-30
    Then Discord was told 1 weekly digest

  Scenario: The digest is posted once a week, however often the tick runs
    Given the treasurer has connected Discord, posting the weekly digest
    When the tick runs on 2026-09-28 at 13:00
    And the tick runs on 2026-09-28 at 14:00
    And the tick runs on 2026-09-29
    Then Discord was told 1 weekly digest

  Scenario: The next week's tick posts the next week's digest
    Given the treasurer has connected Discord, posting the weekly digest
    When the tick runs on 2026-09-28 at 13:00
    And the tick runs on 2026-10-05 at 13:00
    Then Discord was told 2 weekly digests

  Scenario: A league that did not ask for the digest is not posted one
    Given the treasurer has connected Discord
    When the tick runs on 2026-09-28 at 13:00
    Then Discord was told:
      | message                                                         |
      | Hello from BallBank. Holland Hogs is connected to this channel. |

  Scenario: A league that disconnected Discord is not posted a digest
    Given the treasurer has connected Discord, posting the weekly digest
    And the treasurer has disconnected Discord
    When the tick runs on 2026-09-28 at 13:00
    Then Discord was told:
      | message                                                         |
      | Hello from BallBank. Holland Hogs is connected to this channel. |
