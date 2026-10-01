using BallBank.Domain.Notifications;

namespace BallBank.Domain.Tests.Notifications;

public class SmsRepliesTests
{
    [Theory]
    [InlineData("STOP")]
    [InlineData("stop")]
    [InlineData("  Stop \r\n")]
    [InlineData("STOPALL")]
    [InlineData("UNSUBSCRIBE")]
    [InlineData("CANCEL")]
    [InlineData("END")]
    [InlineData("QUIT")]
    public void The_words_Twilio_treats_as_opting_out_stop_the_texts(string body)
    {
        SmsReplies.Classify(body).ShouldBe(SmsReply.Stop);
    }

    [Theory]
    [InlineData("START")]
    [InlineData("start")]
    [InlineData(" Start ")]
    [InlineData("YES")]
    [InlineData("UNSTOP")]
    public void The_words_Twilio_treats_as_opting_back_in_resume_the_texts(string body)
    {
        SmsReplies.Classify(body).ShouldBe(SmsReply.Start);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("HELP")]
    [InlineData("Thanks!")]
    [InlineData("please stop")]
    [InlineData("stop texting me")]
    [InlineData("stopping by later")]
    [InlineData("starting lineup?")]
    public void Anything_else_is_not_an_instruction(string? body)
    {
        SmsReplies.Classify(body).ShouldBe(SmsReply.Other);
    }
}
