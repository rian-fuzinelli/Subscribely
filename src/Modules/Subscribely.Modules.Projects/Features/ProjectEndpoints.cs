using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Subscribely.Infrastructure.Security;
using Subscribely.Infrastructure.Web;
using Subscribely.Modules.Projects.Domain;
using Subscribely.Modules.Projects.Persistence;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Projects.Features;

internal sealed class ProjectRequest
{
    [Required]
    [StringLength(Project.MaxNameLength, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [StringLength(2_000)]
    public string? Description { get; init; }
}

internal sealed record ProjectResponse(Guid Id, string Name, string? Description, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static ProjectResponse From(Project p) => new(p.Id, p.Name, p.Description, p.CreatedAt, p.UpdatedAt);
}

/// <summary>
/// CRUD do produto demo. API key do tenant ou sessão de um TenantAdmin (playground do portal); o gate de produto
/// (status da assinatura + quota) roda antes de cada handler nos dois casos.
/// </summary>
internal static class ProjectEndpoints
{
    private const int MaxListSize = 200;

    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1/projects").WithTags("Projects (product)").RequireProductAccess().AsProductEndpoints();
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/", CreateAsync).WithRequestValidation<ProjectRequest>();
        group.MapPut("/{id:guid}", UpdateAsync).WithRequestValidation<ProjectRequest>();
        group.MapDelete("/{id:guid}", DeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(ProjectsDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Projects.AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Take(MaxListSize)
            .Select(p => new ProjectResponse(p.Id, p.Name, p.Description, p.CreatedAt, p.UpdatedAt))
            .ToListAsync(ct));

    private static async Task<IResult> GetAsync(Guid id, ProjectsDbContext db, CancellationToken ct) =>
        await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) is { } project
            ? TypedResults.Ok(ProjectResponse.From(project))
            : NotFound().ToProblem();

    private static async Task<IResult> CreateAsync(ProjectRequest request, ProjectsDbContext db, TimeProvider time, CancellationToken ct)
    {
        var project = Project.Create(request.Name, request.Description, time.GetUtcNow());
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/v1/projects/{project.Id}", ProjectResponse.From(project));
    }

    private static async Task<IResult> UpdateAsync(Guid id, ProjectRequest request, ProjectsDbContext db, TimeProvider time, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null)
        {
            return NotFound().ToProblem();
        }

        project.Update(request.Name, request.Description, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ProjectResponse.From(project));
    }

    private static async Task<IResult> DeleteAsync(Guid id, ProjectsDbContext db, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null)
        {
            return NotFound().ToProblem();
        }

        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static Error NotFound() => Error.NotFound("project-not-found", "Project not found.");
}
