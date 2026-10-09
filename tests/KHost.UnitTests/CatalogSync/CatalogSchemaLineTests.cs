using System.Text.Json;
using KHost.CatalogSync;

namespace KHost.UnitTests.CatalogSync;

public class CatalogSchemaLineTests
{
    private const string Schema = "https://raw.githubusercontent.com/riddlemd/KHost.Releases/main/plugins.schema.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerOptions.Web) { WriteIndented = true };

    [Fact]
    public void Read_FileWithSchemaLine_ReturnsIt()
        => Assert.Equal(Schema, CatalogSchemaLine.Read($$"""{ "$schema": "{{Schema}}", "schemaVersion": 1, "plugins": [] }"""));

    [Fact]
    public void Read_FileWithoutSchemaLine_ReturnsNull()
        => Assert.Null(CatalogSchemaLine.Read("""{ "schemaVersion": 1, "plugins": [] }"""));

    [Fact]
    public void Read_SchemaThatIsNotAString_ReturnsNull()
        => Assert.Null(CatalogSchemaLine.Read("""{ "$schema": 7, "plugins": [] }"""));

    [Fact]
    public void Prepend_Schema_PutsItFirstAndKeepsEveryOtherProperty()
    {
        var written = CatalogSchemaLine.Prepend("""{ "schemaVersion": 1, "plugins": [ { "name": "KHost's" } ] }""", Schema, Options);

        using var document = JsonDocument.Parse(written);
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal(["$schema", "schemaVersion", "plugins"], names);
        Assert.Equal(Schema, document.RootElement.GetProperty("$schema").GetString());
        Assert.Equal("KHost's", document.RootElement.GetProperty("plugins")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void Prepend_NoSchema_LeavesTheTextAlone()
    {
        const string serialized = """{ "schemaVersion": 1 }""";

        Assert.Same(serialized, CatalogSchemaLine.Prepend(serialized, null, Options));
    }
}
