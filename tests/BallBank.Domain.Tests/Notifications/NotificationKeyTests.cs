using BallBank.Domain.Notifications;

namespace BallBank.Domain.Tests.Notifications;

public class NotificationKeyTests
{
    private static readonly Guid League = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Cause = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void The_same_recipient_cause_and_channel_make_the_same_key() =>
        NotificationKey.For(NotificationKinds.PaymentConfirmed, Cause, Channels.Discord, League)
            .ShouldBe(NotificationKey.For(NotificationKinds.PaymentConfirmed, Cause, Channels.Discord, League));

    [Fact]
    public void Another_channel_is_another_key() =>
        NotificationKey.For(NotificationKinds.PaymentConfirmed, Cause, Channels.Discord, League)
            .ShouldNotBe(NotificationKey.For(NotificationKinds.PaymentConfirmed, Cause, "Sms", League));

    [Fact]
    public void Another_cause_is_another_key() =>
        NotificationKey.For(NotificationKinds.PaymentConfirmed, Cause, Channels.Discord, League)
            .ShouldNotBe(NotificationKey.For(NotificationKinds.PaymentConfirmed, Guid.NewGuid(), Channels.Discord, League));

    [Fact]
    public void Another_recipient_is_another_key() =>
        NotificationKey.For(NotificationKinds.SeasonOpened, Cause, Channels.Discord, League)
            .ShouldNotBe(NotificationKey.For(NotificationKinds.SeasonOpened, Cause, Channels.Discord, Guid.NewGuid()));

    [Fact]
    public void The_key_reads_as_what_it_is_made_of() =>
        NotificationKey.For(NotificationKinds.SeasonOpened, Cause, Channels.Discord, League)
            .ShouldBe("Discord/11111111-1111-1111-1111-111111111111/SeasonOpened/22222222-2222-2222-2222-222222222222");
}
