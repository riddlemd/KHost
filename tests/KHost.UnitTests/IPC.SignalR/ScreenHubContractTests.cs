using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using KHost.IPC.SignalR;

namespace KHost.UnitTests.IPC.SignalR;

// ScreenCommandJsonContext is the only resolver, so a hub method whose types are missing from it
// aborts the connection at call time rather than failing to compile.
public class ScreenHubContractTests
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        TypeInfoResolver = ScreenCommandJsonContext.Default,
    };

    public static TheoryData<Type> HubMethodTypes
    {
        get
        {
            var data = new TheoryData<Type>();
            var seen = new HashSet<Type>();

            foreach (var method in typeof(ScreenHub).GetMethods(BindingFlags.Public | BindingFlags.Instance
                         | BindingFlags.DeclaredOnly))
            {
                // Hub lifecycle overrides are called by SignalR, not a client.
                if (method.GetBaseDefinition().DeclaringType != method.DeclaringType) continue;

                foreach (var type in method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType))
                {
                    var effective = Nullable.GetUnderlyingType(type) ?? type;
                    if (effective == typeof(void) || effective == typeof(Task)) continue;
                    if (seen.Add(effective)) data.Add(effective);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(HubMethodTypes))]
    public void EveryHubMethodType_IsResolvableByThePayloadContext(Type type)
    {
        var info = PayloadOptions.TypeInfoResolver!.GetTypeInfo(type, PayloadOptions);

        Assert.True(info is not null,
            $"ScreenHub uses {type.Name} but ScreenCommandJsonContext has no entry for it, so the "
            + "hub will abort the connection when that method is invoked.");
    }

    // A screen already installed listens under these names, so a rename strands it silently.
    private static readonly Dictionary<string, string> PinnedClientMethods = new()
    {
        [nameof(ScreenClientMethods.Session)] = "Session",
        [nameof(ScreenClientMethods.ReceiveCommand)] = "ReceiveCommand",
        [nameof(ScreenClientMethods.RegistrationRefused)] = "RegistrationRefused",
    };

    public static TheoryData<string> ClientMethodConstants
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var constant in ClientMethodFields()) data.Add(constant.Name);
            return data;
        }
    }

    [Fact]
    public void ScreenClientMethods_KeepTheNamesTheScreenListensOn()
    {
        var actual = ClientMethodFields().ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

        Assert.Equal(PinnedClientMethods.OrderBy(p => p.Key), actual.OrderBy(p => p.Key));
    }

    [Theory]
    [MemberData(nameof(ClientMethodConstants))]
    public void AClientMethod_IsNamedThroughTheConstant_OnBothSides(string constant)
    {
        var reference = $"ScreenClientMethods.{constant}";
        var client = File.ReadAllText(Path.Combine(SignalRSourceDirectory(), "ScreenClient.cs"));
        var server = File.ReadAllText(Path.Combine(SignalRSourceDirectory(), "ScreenHub.cs"))
            + File.ReadAllText(Path.Combine(SignalRSourceDirectory(), "ScreenServerService.cs"));

        Assert.Contains(reference, client);
        Assert.Contains(reference, server);
    }

    [Theory]
    [MemberData(nameof(ClientMethodConstants))]
    public void AClientMethod_IsNeverSpelledOutAsALiteral(string constant)
    {
        var literal = new Regex($"\"{Regex.Escape(PinnedClientMethods[constant])}\"");

        var spelled = Directory.EnumerateFiles(SignalRSourceDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && Path.GetFileName(path) != "ScreenClientMethods.cs")
            .Where(path => literal.IsMatch(File.ReadAllText(path)))
            .Select(Path.GetFileName);

        Assert.Empty(spelled);
    }

    private static IEnumerable<FieldInfo> ClientMethodFields()
        => typeof(ScreenClientMethods).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string));

    // Walked up rather than hardcoded: the depth from the test binary to the root changes with
    // BaseOutputPath, which this repo's build redirects.
    private static string SignalRSourceDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (directory.GetFiles("KHost.slnx").Length > 0)
                return Path.Combine(directory.FullName, "src", "KHost.IPC.SignalR");
        }

        throw new InvalidOperationException(
            $"No KHost.slnx above {AppContext.BaseDirectory}, so the repository root could not be found.");
    }
}
