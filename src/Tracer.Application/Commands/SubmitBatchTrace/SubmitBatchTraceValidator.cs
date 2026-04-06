using FluentValidation;
using Tracer.Application.DTOs;

namespace Tracer.Application.Commands.SubmitBatchTrace;

/// <summary>
/// Validates a <see cref="SubmitBatchTraceCommand"/> before it reaches the handler.
/// </summary>
public sealed class SubmitBatchTraceValidator : AbstractValidator<SubmitBatchTraceCommand>
{
    private const int MaxBatchSize = 200;

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

    public SubmitBatchTraceValidator()
    {
        RuleFor(x => x.Source)
            .NotEmpty().WithMessage("Source is required.")
            .MaximumLength(100).WithMessage("Source must not exceed 100 characters.")
            .Matches(@"^[a-zA-Z0-9\-_.]+$").WithMessage("Source may only contain letters, digits, hyphens, underscores, and dots.")
            .When(x => !string.IsNullOrEmpty(x.Source));

        RuleFor(x => x.Items)
            .NotNull().WithMessage("Items are required.")
            .Must(items => items.Count >= 1).WithMessage("Batch must contain at least 1 item.")
            .Must(items => items.Count <= MaxBatchSize)
            .WithMessage($"Batch size cannot exceed {MaxBatchSize} items.");

        When(x => x.Items is { Count: > 0 }, () =>
        {
            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(x => x)
                    .Must(HasAtLeastOneIdentifyingField)
                    .WithMessage("Each item must provide at least one identifying field (CompanyName, RegistrationId, TaxId, Phone, Email, or Website).");

                item.RuleFor(x => x.CompanyName).MaximumLength(500);
                item.RuleFor(x => x.Phone).MaximumLength(50);
                item.RuleFor(x => x.Email).MaximumLength(320);
                item.RuleFor(x => x.Website).MaximumLength(2000);
                item.RuleFor(x => x.Address).MaximumLength(500);
                item.RuleFor(x => x.City).MaximumLength(200);
                item.RuleFor(x => x.RegistrationId).MaximumLength(50);
                item.RuleFor(x => x.TaxId).MaximumLength(50);
                item.RuleFor(x => x.IndustryHint).MaximumLength(200);

                item.RuleFor(x => x.Country)
                    .Must(c => c is null || ValidIsoCodes.Contains(c))
                    .WithMessage("Country must be a valid ISO 3166-1 alpha-2 code.");

                item.RuleFor(x => x.CallbackUrl)
                    .Must(u => u is null || u.Scheme == Uri.UriSchemeHttps)
                    .WithMessage("Callback URL must use HTTPS.");

                item.RuleFor(x => x.Depth).IsInEnum().WithMessage("Invalid trace depth.");
            });
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
