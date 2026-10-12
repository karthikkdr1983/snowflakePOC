using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using TimeWise.SnowflakePoc.Api.Identity;

namespace TimeWise.SnowflakePoc.Api.Infrastructure;

/// <summary>Adds the X-Test-User-Email header box to the /api/me endpoints in Swagger.</summary>
public sealed class TestUserHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.RelativePath?.StartsWith("api/me", StringComparison.OrdinalIgnoreCase) != true)
            return;

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = TestHeaderUserProvider.HeaderName,
            In = ParameterLocation.Header,
            Required = true,
            Description = "POC only: the email of the user to act as (replaced by Entra ID in Phase 2).",
            Schema = new OpenApiSchema { Type = "string", Example = new OpenApiString("rahul.taylor@timewise-demo.com") }
        });
    }
}
