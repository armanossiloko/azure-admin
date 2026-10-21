using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AzureAdmin.API.Infrastructure;

/// <summary>
/// Marks every non-nullable property as <c>required</c> in the OpenAPI schema. The DTOs are C# records
/// with nullable reference types enabled, and System.Text.Json always serializes every property, so a
/// property that is not nullable in C# is always present in the JSON. Without this the generated Angular
/// models would make everything optional (<c>id?: string</c>) and force null checks on data that can never
/// be missing. Nullable members (<c>string?</c>, <c>int?</c>) stay optional.
/// </summary>
public sealed class RequireNonNullablePropertiesSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is not OpenApiSchema concrete || concrete.Properties is null || concrete.Properties.Count == 0)
            return;

        concrete.Required ??= new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, property) in concrete.Properties)
        {
            if (!IsNullable(property))
                concrete.Required.Add(name);
        }
    }

    private static bool IsNullable(IOpenApiSchema property) =>
        property.Type is { } type && type.HasFlag(JsonSchemaType.Null);
}
