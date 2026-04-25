using FluentValidation;
using Tracer.Application.DTOs;

namespace Tracer.Application.Commands.SubmitTrace;

/// <summary>
/// Validates a <see cref="SubmitTraceCommand"/> before it reaches the handler.
/// </summary>
public sealed class SubmitTraceValidator : AbstractValidator<SubmitTraceCommand>
{
    private static readonly HashSet<string> ValidIsoCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AD","AE","AF","AG","AI","AL","AM","AO","AQ","AR","AS","AT","AU","AW","AX","AZ",
        "BA","BB","BD","BE","BF","BG","BH","BI","BJ","BL","BM","BN","BO","BQ","BR","BS","BT","BV","BW","BY","BZ",
        "CA","CC","CD","CF","CG","CH","CI","CK","CL","CM","CN","CO","CR","CU","CV","CW","CX","CY","CZ",
        "DE","DJ","DK","DM","DO","DZ",
        "EC","EE","EG","EH","ER","ES","ET",
        "FI","FJ","FK","FM","FO","FR",
        "GA","GB","GD","GE","GF","GG","GH","GI","GL","GM","GN","GP","GQ","GR","GS","GT","GU","GW","GY",
        "HK","HM","HN","HR","HT","HU",
        "ID","IE","IL","IM","IN","IO","IQ","IR","IS","IT",
        "JE","JM","JO","JP",
        "KE","KG","KH","KI","KM","KN","KP","KR","KW","KY","KZ",
        "LA","LB","LC","LI","LK","LR","LS","LT","LU","LV","LY",
        "MA","MC","MD","ME","MF","MG","MH","MK","ML","MM","MN","MO","MP","MQ","MR","MS","MT","MU","MV","MW","MX","MY","MZ",
        "NA","NC","NE","NF","NG","NI","NL","NO","NP","NR","NU","NZ",
        "OM",
        "PA","PE","PF","PG","PH","PK","PL","PM","PN","PR","PS","PT","PW","PY",
        "QA",
        "RE","RO","RS","RU","RW",
        "SA","SB","SC","SD","SE","SG","SH","SI","SJ","SK","SL","SM","SN","SO","SR","SS","ST","SV","SX","SY","SZ",
        "TC","TD","TF","TG","TH","TJ","TK","TL","TM","TN","TO","TR","TT","TV","TW","TZ",
        "UA","UG","UM","US","UY","UZ",
        "VA","VC","VE","VG","VI","VN","VU",
        "WF","WS",
        "YE","YT",
        "ZA","ZM","ZW",
    };

    public SubmitTraceValidator()
    {
        RuleFor(x => x.Input)
            .NotNull()
            .WithMessage("Input is required.");

        RuleFor(x => x.Source)
            .NotEmpty()
            .WithMessage("Source is required.");

        When(x => x.Input is not null, () =>
        {
            RuleFor(x => x.Input)
                .Must(HasAtLeastOneIdentifyingField)
                .WithMessage("At least one identifying field (CompanyName, RegistrationId, TaxId, Phone, Email, or Website) must be provided.");

            RuleFor(x => x.Input.CompanyName).MaximumLength(500);
            RuleFor(x => x.Input.Phone).MaximumLength(50);
            RuleFor(x => x.Input.Email).MaximumLength(320);
            // Website must be a valid HTTP or HTTPS absolute URL when supplied.
            // Defence-in-depth: WebScraperClient also validates, but rejecting at the API
            // boundary prevents malformed values reaching the infrastructure layer at all.
            RuleFor(x => x.Input.Website)
                .MaximumLength(2000)
                .Must(w => w is null || (Uri.TryCreate(w, UriKind.Absolute, out var u)
                                         && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)))
                .WithMessage("Website must be a valid HTTP or HTTPS URL.");
            RuleFor(x => x.Input.Address).MaximumLength(500);
            RuleFor(x => x.Input.City).MaximumLength(200);
            RuleFor(x => x.Input.RegistrationId).MaximumLength(50);
            RuleFor(x => x.Input.TaxId).MaximumLength(50);
            RuleFor(x => x.Input.IndustryHint).MaximumLength(200);

            RuleFor(x => x.Input.Country)
                .Must(c => c is null || ValidIsoCodes.Contains(c))
                .WithMessage("Country must be a valid ISO 3166-1 alpha-2 code.");

            RuleFor(x => x.Input.CallbackUrl)
                .Must(u => u is null || u.Scheme == Uri.UriSchemeHttps)
                .WithMessage("Callback URL must use HTTPS.");

            RuleFor(x => x.Input.Depth)
                .IsInEnum()
                .WithMessage("Invalid trace depth.");
        });
    }

    private static bool HasAtLeastOneIdentifyingField(TraceRequestDto input) =>
        !string.IsNullOrWhiteSpace(input.CompanyName) ||
        !string.IsNullOrWhiteSpace(input.RegistrationId) ||
        !string.IsNullOrWhiteSpace(input.TaxId) ||
        !string.IsNullOrWhiteSpace(input.Phone) ||
        !string.IsNullOrWhiteSpace(input.Email) ||
        !string.IsNullOrWhiteSpace(input.Website);
}
