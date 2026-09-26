using System.Net.Mail;

namespace BallBank.Domain.Membership;

/// <summary>How to reach a member, normalised: <c>null</c> where nothing is recorded.</summary>
/// <param name="Phone">E.164, e.g. <c>+15550100000</c>.</param>
/// <param name="DiscordUsername">Lowercase, without <c>@</c> or discriminator.</param>
public sealed record ContactDetails(string? Email, string? Phone, string? DiscordUsername)
{
    /// <summary>Nothing recorded.</summary>
    public static readonly ContactDetails None = new(null, null, null);

    /// <summary>Contact details as someone entered them, normalised; a blank field is left unrecorded.</summary>
    public static ContactDetails From(string? email, string? phone, string? discordUsername) =>
        new(
            Blank(email) ? null : NormaliseEmail(email!),
            Blank(phone) ? null : NormalisePhone(phone!),
            Blank(discordUsername) ? null : NormaliseDiscordUsername(discordUsername!));

    /// <summary>One plain email address, without the space around it; no display name, no list.</summary>
    private static string NormaliseEmail(string entered)
    {
        var email = entered.Trim();

        if (!MailAddress.TryCreate(email, out var address) || address.Address != email || email.Any(char.IsWhiteSpace))
        {
            throw new DomainException("That is not an email address.");
        }

        return email;
    }

    /// <summary>
    /// A US phone number, written any common way, in E.164: <c>(555) 010-0000</c>, <c>555.010.0000</c>
    /// and <c>+1 555 010 0000</c> are all <c>+15550100000</c>.
    /// </summary>
    private static string NormalisePhone(string entered)
    {
        var trimmed = entered.Trim();
        var international = trimmed.StartsWith('+');
        var written = international ? trimmed[1..] : trimmed;

        if (!written.All(c => char.IsAsciiDigit(c) || c is ' ' or '(' or ')' or '-' or '.'))
        {
            throw NotAUsNumber();
        }

        var digits = new string(written.Where(char.IsAsciiDigit).ToArray());

        // +44 …, or 011 44 … as dialled from the US: a number in another country.
        if ((international && !digits.StartsWith('1')) || (!international && digits.StartsWith("011", StringComparison.Ordinal)))
        {
            throw new DomainException("BallBank takes US phone numbers only.");
        }

        var national = digits.Length == 11 && digits.StartsWith('1') ? digits[1..] : digits;

        // Ten digits, and no US area code starts with 0 or 1.
        if (national.Length != 10 || national[0] is '0' or '1')
        {
            throw NotAUsNumber();
        }

        return "+1" + national;
    }

    /// <summary>
    /// A Discord username as Discord shows it since usernames became unique: lowercase, and without the
    /// <c>@</c> people type before it or the <c>#1234</c> discriminator older usernames carried.
    /// </summary>
    private static string NormaliseDiscordUsername(string entered)
    {
        var username = entered.Trim().TrimStart('@');

        var discriminator = username.LastIndexOf('#');
        if (discriminator >= 0 && username[(discriminator + 1)..].All(char.IsAsciiDigit))
        {
            username = username[..discriminator];
        }

        username = username.ToLowerInvariant();

        if (username.Length is < 2 or > 32
            || !username.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or '.')
            || username.Contains("..", StringComparison.Ordinal))
        {
            throw new DomainException("That is not a Discord username: 2 to 32 letters, numbers, underscores and periods.");
        }

        return username;
    }

    private static bool Blank(string? entered) => string.IsNullOrWhiteSpace(entered);

    private static DomainException NotAUsNumber() =>
        new("That is not a US phone number. Enter the 10 digits, like (555) 010-0000.");
}
