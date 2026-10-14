using Bunit;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Services;
using KHost.Domain.Services.Messaging;
using KHost.UserInterface.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

// The parent namespace of every component fixture, so none needs a using to reach it.
namespace KHost.UnitTests.UserInterface;

/// <summary>What <see cref="KHostTestDefaults.AddKHostTestDefaults"/> registered, for a fixture to
/// stub or assert on.</summary>
internal sealed record KHostTestServices(
    IMessageBroker Broker,
    IFlashService Flash,
    IDialogService Dialogs,
    IUsersService Users,
    IMediaService Media);

/// <summary>The setup nearly every console component fixture repeats. An extension, not a base class:
/// <see cref="BunitContext"/>'s <c>Dispose</c> is not virtual, so a shared base could not own cleanup,
/// and a fixture whose setup differs simply does not call it.</summary>
internal static class KHostTestDefaults
{
    /// <summary>Loose JS interop, a real broker, and a substitute for each service the record names.
    /// Pass a fixture's own instance to have that one registered instead. Call it first: a service
    /// registered afterwards replaces the default, one registered before is replaced by it.</summary>
    public static KHostTestServices AddKHostTestDefaults(
        this BunitContext context,
        IMessageBroker? broker = null,
        IFlashService? flash = null,
        IDialogService? dialogs = null,
        IUsersService? users = null,
        IMediaService? media = null)
    {
        // Panels call JS on first render; strict mode throws on every call nothing planned for.
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var services = new KHostTestServices(
            broker ?? new MessageBroker(NullLogger<MessageBroker>.Instance),
            flash ?? Substitute.For<IFlashService>(),
            dialogs ?? Substitute.For<IDialogService>(),
            users ?? Substitute.For<IUsersService>(),
            media ?? Substitute.For<IMediaService>());

        context.Services.AddSingleton(services.Broker);
        context.Services.AddSingleton(services.Flash);
        context.Services.AddSingleton(services.Dialogs);
        context.Services.AddSingleton(services.Users);
        context.Services.AddSingleton(services.Media);

        return services;
    }
}
