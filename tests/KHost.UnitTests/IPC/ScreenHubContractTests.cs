using System.Reflection;
using System.Text.Json;
using KHost.IPC.SignalR;

namespace KHost.UnitTests.IPC;

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
}
