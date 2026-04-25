using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Tracer.Api.Middleware;

namespace Tracer.Infrastructure.Tests.Security;

public sealed class ApiKeyOptionsBinderTests
{
    private const string ValidKey = "abcdef0123456789"; // 16 chars (minimum)
    private const string AnotherValidKey = "ZYXWVUTSR1234567";

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Bind_FlatStringForm_ProducesEntriesWithoutMetadata()
    {
        var config = BuildConfig(new()
        {
            ["Auth:ApiKeys:0"] = ValidKey,
            ["Auth:ApiKeys:1"] = AnotherValidKey,
        });

        var options = ApiKeyOptionsBinder.Bind(config);

        options.ApiKeys.Should().HaveCount(2);
        options.ApiKeys[0].Key.Should().Be(ValidKey);
        options.ApiKeys[0].Label.Should().BeNull();
        options.ApiKeys[0].ExpiresAt.Should().BeNull();
        options.ApiKeys[1].Key.Should().Be(AnotherValidKey);
    }

    [Fact]
    public void Bind_StructuredForm_CapturesLabelAndExpiry()
    {
        var config = BuildConfig(new()
        {
            ["Auth:ApiKeys:0:Key"] = ValidKey,
            ["Auth:ApiKeys:0:Label"] = "ci",
            ["Auth:ApiKeys:0:ExpiresAt"] = "2030-01-02T03:04:05Z",
        });

        var options = ApiKeyOptionsBinder.Bind(config);

        options.ApiKeys.Should().ContainSingle();
        var entry = options.ApiKeys[0];
        entry.Key.Should().Be(ValidKey);
        entry.Label.Should().Be("ci");
        entry.ExpiresAt.Should().Be(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero));
    }

    [Fact]
    public void Bind_MixedFormsInSameArray_BothAreAccepted()
    {
        var config = BuildConfig(new()
        {
            ["Auth:ApiKeys:0"] = ValidKey,
            ["Auth:ApiKeys:1:Key"] = AnotherValidKey,
            ["Auth:ApiKeys:1:Label"] = "internal-ops",
        });

        var options = ApiKeyOptionsBinder.Bind(config);

        options.ApiKeys.Should().HaveCount(2);
        options.ApiKeys[0].Key.Should().Be(ValidKey);
        options.ApiKeys[0].Label.Should().BeNull();
        options.ApiKeys[1].Key.Should().Be(AnotherValidKey);
        options.ApiKeys[1].Label.Should().Be("internal-ops");
    }

    [Fact]
    public void Bind_EmptySection_ProducesEmptyList()
    {
        var config = BuildConfig([]);

        var options = ApiKeyOptionsBinder.Bind(config);

        options.ApiKeys.Should().BeEmpty();
    }

    [Fact]
    public void Bind_StructuredEntryMissingKey_Throws()
    {
        var config = BuildConfig(new()
        {
            ["Auth:ApiKeys:0:Label"] = "orphan",
        });

        var act = () => ApiKeyOptionsBinder.Bind(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ApiKeys['0']:Key is missing*");
    }

    [Fact]
    public void Bind_StructuredEntryInvalidExpiresAt_Throws()
    {
        var config = BuildConfig(new()
        {
            ["Auth:ApiKeys:0:Key"] = ValidKey,
            ["Auth:ApiKeys:0:ExpiresAt"] = "not-a-date",
        });

        var act = () => ApiKeyOptionsBinder.Bind(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ApiKeys['0']:ExpiresAt*not a valid ISO 8601*");
    }

    [Fact]
    public void Validate_HappyPath_ReturnsNull()
    {
        var options = new ApiKeyOptions
        {
            ApiKeys = [new() { Key = ValidKey }, new() { Key = AnotherValidKey }],
        };

        var error = ApiKeyOptionsBinder.Validate(options, DateTimeOffset.UtcNow);

        error.Should().BeNull();
    }

    [Fact]
    public void Validate_KeyTooShort_ReturnsError()
    {
        var options = new ApiKeyOptions
        {
            ApiKeys = [new() { Key = "short" }],
        };

        var error = ApiKeyOptionsBinder.Validate(options, DateTimeOffset.UtcNow);

        error.Should().NotBeNull().And.Contain("at least 16 characters");
    }

    [Fact]
    public void Validate_DuplicateKey_ReturnsError()
    {
        var options = new ApiKeyOptions
        {
            ApiKeys = [new() { Key = ValidKey }, new() { Key = ValidKey, Label = "dup" }],
        };

        var error = ApiKeyOptionsBinder.Validate(options, DateTimeOffset.UtcNow);

        error.Should().NotBeNull().And.Contain("duplicate");
    }

    [Fact]
    public void Validate_AlreadyExpired_ReturnsError()
    {
        var now = new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero);
        var options = new ApiKeyOptions
        {
            ApiKeys =
            [
                new()
                {
                    Key = ValidKey,
                    ExpiresAt = now.AddDays(-1),
                },
            ],
        };

        var error = ApiKeyOptionsBinder.Validate(options, now);

        error.Should().NotBeNull().And.Contain("ExpiresAt is in the past");
    }

    [Fact]
    public void Validate_ExpiryInFuture_IsAccepted()
    {
        var now = new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero);
        var options = new ApiKeyOptions
        {
            ApiKeys =
            [
                new()
                {
                    Key = ValidKey,
                    ExpiresAt = now.AddDays(7),
                },
            ],
        };

        var error = ApiKeyOptionsBinder.Validate(options, now);

        error.Should().BeNull();
    }

    [Fact]
    public void IsActive_NoExpiry_AlwaysActive()
    {
        var entry = new ApiKeyEntry { Key = ValidKey };

        entry.IsActive(DateTimeOffset.UtcNow.AddYears(100)).Should().BeTrue();
    }

    [Fact]
    public void IsActive_ExpiryInFuture_IsActive()
    {
        var now = DateTimeOffset.UtcNow;
        var entry = new ApiKeyEntry { Key = ValidKey, ExpiresAt = now.AddMinutes(1) };

        entry.IsActive(now).Should().BeTrue();
    }

    [Fact]
    public void IsActive_ExpiryAtExactlyNow_IsInactive()
    {
        var now = DateTimeOffset.UtcNow;
        var entry = new ApiKeyEntry { Key = ValidKey, ExpiresAt = now };

        entry.IsActive(now).Should().BeFalse();
    }
}
