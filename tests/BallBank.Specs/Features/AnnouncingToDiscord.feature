Feature: Announcing to Discord
  The treasurer connects the league's Discord channel once, and BallBank speaks there for the books: a
  hello at once, then the season when it opens, and each payment the treasurer confirms with what the pot
  now holds. A payment is announced only once the treasurer has confirmed it, and only if the treasurer
  asked for payments to be announced.

  Background:
    Given a league "Holland Hogs" with members Jacob, Sam

  Scenario: Connecting Discord says hello
    When the treasurer connects Discord
    Then Discord was told:
      | message                                                         |
      | Hello from BallBank. Holland Hogs is connected to this channel. |

  Scenario: Opening a season announces the dues and when they are due
    Given the treasurer has connected Discord
    When the treasurer opens the "2026" season with dues of $50 due on 2026-10-01
    Then Discord was told:
      | message                                                                |
      | Hello from BallBank. Holland Hogs is connected to this channel.        |
      | Holland Hogs opened the 2026 season. Dues are $50.00, due Oct 1, 2026. |

  Scenario: Confirming a payment announces the team, the amount and the pot
    Given season dues of $50 due on 2026-10-01
    And the treasurer has connected Discord, announcing confirmed payments
    When Sam attests a $50 Venmo payment with reference "VN-1234"
    And the treasurer confirms Sam's payment
    And Jacob attests a $30 Cash payment
    And the treasurer confirms Jacob's payment
    Then Discord was told:
      | message                                                                           |
      | Hello from BallBank. Holland Hogs is connected to this channel.                   |
      | Sam's Slammers paid $50.00 and the treasurer confirmed it. The pot is now $50.00. |
      | Hog Wild paid $30.00 and the treasurer confirmed it. The pot is now $80.00.       |

  Scenario: A payment is not announced until the treasurer confirms it
    Given season dues of $50 due on 2026-10-01
    And the treasurer has connected Discord, announcing confirmed payments
    When Sam attests a $50 Venmo payment with reference "VN-1234"
    Then Discord was told:
      | message                                                         |
      | Hello from BallBank. Holland Hogs is connected to this channel. |

  Scenario: A payment the treasurer rejects is never announced
    Given season dues of $50 due on 2026-10-01
    And the treasurer has connected Discord, announcing confirmed payments
    When Sam attests a $50 Zelle payment with reference "ZL-0000"
    And the treasurer rejects Sam's payment because "No Zelle with that reference arrived"
    Then Discord was told:
      | message                                                         |
      | Hello from BallBank. Holland Hogs is connected to this channel. |

  Scenario: Payments are not announced unless the treasurer asked for it
    Given season dues of $50 due on 2026-10-01
    And the treasurer has connected Discord
    When Sam attests a $50 Venmo payment with reference "VN-1234"
    And the treasurer confirms Sam's payment
    Then Discord was told:
      | message                                                         |
      | Hello from BallBank. Holland Hogs is connected to this channel. |

  Scenario: Confirming a payment twice announces it once
    Given season dues of $50 due on 2026-10-01
    And the treasurer has connected Discord, announcing confirmed payments
    When Sam attests a $50 Venmo payment with reference "VN-1234"
    And the treasurer confirms Sam's payment
    And the treasurer confirms Sam's payment
    Then Discord was told:
      | message                                                                           |
      | Hello from BallBank. Holland Hogs is connected to this channel.                   |
      | Sam's Slammers paid $50.00 and the treasurer confirmed it. The pot is now $50.00. |
