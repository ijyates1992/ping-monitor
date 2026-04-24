using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using PingMonitor.Web.Services.Time;
using PingMonitor.Web.ViewModels.Profile;
using Xunit;

namespace PingMonitor.Web.Tests;

public sealed class ProfileDisplayPreferencesTests
{
    [Fact]
    public void UpdateProfileDisplayPreferencesInputModel_AllowsPostingWithoutEmail()
    {
        var model = new UpdateProfileDisplayPreferencesInputModel
        {
            DisplayTimeZoneId = "Europe/London"
        };

        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), validationResults, validateAllProperties: true);

        Assert.True(isValid);
        Assert.Empty(validationResults);
    }

    [Fact]
    public void UserTimeZoneService_IncludesUtcAndEuropeLondon_WithoutDuplicates()
    {
        var service = new UserTimeZoneService(new HttpContextAccessor(), userManager: null!);

        var options = service.GetSelectableTimeZoneOptions();

        Assert.NotEmpty(options);
        Assert.Equal("UTC", options[0].Value);
        Assert.Equal("Europe/London", options[1].Value);
        Assert.Equal("United Kingdom — London (Europe/London)", options[1].Label);
        Assert.Equal(1, options.Count(option => string.Equals(option.Value, "Europe/London", StringComparison.Ordinal)));
    }
}
