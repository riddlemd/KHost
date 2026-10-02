using Bunit;
using KHost.Abstractions.Services;
using KHost.UserInterface.Components.Pages.Settings;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KHost.UnitTests.UserInterface.Components.Pages.Settings;

/// <summary>Each number carries the bounds AppSettingsService clamps it to on save.</summary>
public class AppSettingsPageRangeTests : BunitContext
{
    public AppSettingsPageRangeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var settings = Substitute.For<IAppSettingsService>();
        settings.Current.Returns(_ => new AppSettings());
        settings.DefaultMediaDirectory.Returns("/karaoke");

        Services.AddSingleton(settings);
        Services.AddSingleton(Substitute.For<IDialogService>());
        Services.AddSingleton(Substitute.For<IFlashService>());
        Services.AddSingleton(Substitute.For<IUsersService>());
        Services.AddFFmpegSection();
        Services.AddSearchSection();
    }

    [Fact]
    public void EveryNumber_HasAMinimumAndAMaximum()
    {
        var page = Render<AppSettingsPage>();

        var numbers = page.FindAll("input.kh-app-settings__number");

        Assert.NotEmpty(numbers);
        Assert.All(numbers, input =>
        {
            Assert.False(string.IsNullOrEmpty(input.GetAttribute("min")));
            Assert.False(string.IsNullOrEmpty(input.GetAttribute("max")));
        });
    }

    [Fact]
    public void StopFadeAndSegment_AreBoundedAsTheServiceClamps()
    {
        var page = Render<AppSettingsPage>();

        var maxima = page.FindAll("input.kh-app-settings__number").Select(input => input.GetAttribute("max")).ToList();

        Assert.Contains($"{AppSettings.MaxStopFadeSeconds}", maxima);
        Assert.Contains($"{AppSettings.MaxSegmentSeconds}", maxima);
        Assert.Contains($"{AppSettings.MaxAdDurationSeconds}", maxima);
        Assert.Contains($"{AppSettings.MaxPageSize}", maxima);
    }
}
