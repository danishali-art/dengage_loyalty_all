// @vitest-environment node
/**
 * The generator against small C# fixtures shaped like the real backend (Tiers, RuleCalculation,
 * Api.Framework pagination/errors). These pin the mapping rules in build-spec.ts and show that a
 * C# change shows up in the spec — which is what `api:check` relies on to catch drift.
 */
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { buildSpec, serializeSpec } from './build-spec';

const NO_OVERRIDES = { stringUnions: {}, excludeModules: [] };

const FRAMEWORK = {
  'src/dEngage.Loyalty.Api.Framework/Pagination/Pagination.cs': `
namespace dEngage.Loyalty.Api.Framework.Pagination;
public sealed class PageRequest { public int Page { get; init; } = 1; public int PageSize { get; init; } = 25; }
public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Data { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int Total { get; init; }
}`,
  'src/dEngage.Loyalty.Api.Framework/ErrorHandling/ErrorResponseFactory.cs': `
namespace dEngage.Loyalty.Api.Framework.ErrorHandling;
public sealed class ProblemDetailsBody
{
    public string Type { get; init; } = "";
    public string Title { get; init; } = "";
    public int Status { get; init; }
    public string Code { get; init; } = "";
    public string TraceId { get; init; } = "";
    public Dictionary<string, string[]>? Errors { get; init; }
}`,
};

const TIERS = {
  'src/dEngage.Loyalty.Api/Tiers/TiersDtos.cs': `
namespace dEngage.Loyalty.Api.Tiers;
public sealed record CreateTierRequest(
    string Name, string DisplayName, decimal MinPoints,
    string QualifyingModel, int? QualifyingPeriodDays, int GraceDays, int SortOrder = 0);
public sealed record TierResponse(
    Guid Id, string Name, decimal MinPoints, int? QualifyingPeriodDays, DateTime CreatedAt,
    RuleCalculation? Calculation, IReadOnlyList<string> Tags, string Rounding = "down");`,
  'src/dEngage.Loyalty.Api/Tiers/TiersAppService.cs': `
using dEngage.Loyalty.Api.Framework.Pagination;
using dEngage.Loyalty.RuleEngine.Models;
namespace dEngage.Loyalty.Api.Tiers;
public interface ITiersAppService
{
    Task<PagedResult<TierResponse>> ListAsync(string tenantId, Guid programId, PageRequest page, CancellationToken ct);
    Task<TierResponse> CreateAsync(string tenantId, Guid programId, CreateTierRequest request, CancellationToken ct);
    Task DeleteAsync(string tenantId, Guid programId, Guid tierId, CancellationToken ct);
}`,
  'src/dEngage.Loyalty.Api/Tiers/TiersModule.cs': `
namespace dEngage.Loyalty.Api.Tiers;
// /api/v1/tenants/{tenantId}/programs/{programId}/tiers
public sealed class TiersModule : TenantScopedModule
{
    public TiersModule(ITiersAppService appService, IValidator<CreateTierRequest> createValidator)
        : base("/api/v1/tenants/{tenantId}/programs/{programId}/tiers")
    {
        MapGet("", async (parameters, ct) =>
        {
            var programId = RouteGuidParam(parameters, "programId");
            var search = (string?)Request.Query["search"];
            return JsonResponses.Ok(await appService.ListAsync(tenantId, programId, ReadPageRequest(), ct));
        });
        MapPost("", async (parameters, ct) =>
        {
            var request = await Request.ReadValidatedJsonBodyAsync(createValidator, ct);
            return JsonResponses.Ok(await appService.CreateAsync(tenantId, programId, request, ct), HttpStatusCode.Created);
        });
        MapDelete("/{tierId}", async (parameters, ct) =>
        {
            await appService.DeleteAsync(tenantId, RouteGuidParam(parameters, "programId"), RouteGuidParam(parameters, "tierId"), ct);
            return JsonResponses.NoContent();
        });
    }
}`,
  'src/dEngage.Loyalty.RuleEngine/Models/RuleCalculation.cs': `
using System.Text.Json.Serialization;
namespace dEngage.Loyalty.RuleEngine.Models;
public class RuleCalculation
{
    [JsonPropertyName("rate")]
    public decimal? Factor { get; set; }

    // CR comment with a (parenthesis) that must not confuse the reader.
    [JsonPropertyName("per_customer_total")]
    public string? PerCustomerTotal { get; set; }

    public static RuleCalculation Empty() => new();
}`,
  'src/dEngage.Loyalty.Api/Auth/AuthDtos.cs': `
namespace dEngage.Loyalty.Api.Auth;
public sealed record LoginRequest(string Email, string Password);
public sealed record LoginResponse(string Token);`,
  'src/dEngage.Loyalty.Api/Auth/AuthAppService.cs': `
namespace dEngage.Loyalty.Api.Auth;
public interface IAuthAppService { Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct); }`,
  'src/dEngage.Loyalty.Api/Auth/AuthModule.cs': `
namespace dEngage.Loyalty.Api.Auth;
public sealed class AuthModule : NancyModule
{
    public AuthModule(IAuthAppService appService, IValidator<LoginRequest> loginValidator)
    {
        Post("/api/v1/auth/login", async (_, ct) =>
        {
            var request = await Request.ReadValidatedJsonBodyAsync(loginValidator, ct);
            return JsonResponses.Ok(await appService.LoginAsync(request, ct));
        });
    }
}`,
};

let root: string | null = null;

function repo(files: Record<string, string>): string {
  root = mkdtempSync(join(tmpdir(), 'openapi-spec-'));
  for (const [path, content] of Object.entries(files)) {
    const full = join(root, path);
    mkdirSync(dirname(full), { recursive: true });
    writeFileSync(full, content);
  }
  return root;
}

afterEach(() => {
  if (root) rmSync(root, { recursive: true, force: true });
  root = null;
});

type Json = Record<string, any>; // eslint-disable-line @typescript-eslint/no-explicit-any

describe('buildSpec', () => {
  it('maps records the way the API serializes them', () => {
    const spec = buildSpec({ repoRoot: repo({ ...FRAMEWORK, ...TIERS }), overrides: NO_OVERRIDES }) as Json;
    const tier = spec.components.schemas.TierResponse;
    expect(tier.properties.id).toEqual({ type: 'string', format: 'uuid' });
    // DecimalStringJsonConverter: decimals travel as strings.
    expect(tier.properties.minPoints.type).toBe('string');
    expect(tier.properties.createdAt).toEqual({ type: 'string', format: 'date-time' });
    // WhenWritingNull: nullable members are optional; a response always carries defaulted members.
    expect(tier.required).toEqual(['id', 'name', 'minPoints', 'createdAt', 'tags', 'rounding']);
    expect(tier.properties.qualifyingPeriodDays.type).toEqual(['integer', 'null']);
    expect(tier.properties.calculation.anyOf[0]).toEqual({ $ref: '#/components/schemas/RuleCalculation' });
  });

  it('lets a request leave out a defaulted member', () => {
    const spec = buildSpec({ repoRoot: repo({ ...FRAMEWORK, ...TIERS }), overrides: NO_OVERRIDES }) as Json;
    expect(spec.components.schemas.CreateTierRequest.required).not.toContain('sortOrder');
  });

  it('honours [JsonPropertyName] on class properties and skips static members', () => {
    const spec = buildSpec({ repoRoot: repo({ ...FRAMEWORK, ...TIERS }), overrides: NO_OVERRIDES }) as Json;
    expect(Object.keys(spec.components.schemas.RuleCalculation.properties)).toEqual([
      'rate',
      'per_customer_total',
    ]);
  });

  it('reads routes, parameters, bodies and status codes from the module', () => {
    const spec = buildSpec({ repoRoot: repo({ ...FRAMEWORK, ...TIERS }), overrides: NO_OVERRIDES }) as Json;
    const list = spec.paths['/api/v1/tenants/{tenantId}/programs/{programId}/tiers'].get;
    expect(list.parameters.map((p: Json) => `${p.in}:${p.name}`)).toEqual([
      'path:tenantId',
      'path:programId',
      'query:page',
      'query:pageSize',
      'query:search',
    ]);
    expect(list.parameters[1].schema.format).toBe('uuid');
    expect(list.responses['200'].content['application/json'].schema).toEqual({
      $ref: '#/components/schemas/PagedResult_TierResponse',
    });

    const create = spec.paths['/api/v1/tenants/{tenantId}/programs/{programId}/tiers'].post;
    expect(create.requestBody.content['application/json'].schema).toEqual({
      $ref: '#/components/schemas/CreateTierRequest',
    });
    expect(Object.keys(create.responses)).toEqual(['201', 'default']);

    const remove = spec.paths['/api/v1/tenants/{tenantId}/programs/{programId}/tiers/{tierId}'].delete;
    expect(Object.keys(remove.responses)).toEqual(['204', 'default']);
  });

  it('reads a raw Nancy route with its full path, and marks login as unauthenticated', () => {
    const spec = buildSpec({ repoRoot: repo({ ...FRAMEWORK, ...TIERS }), overrides: NO_OVERRIDES }) as Json;
    expect(spec.paths['/api/v1/auth/login'].post.security).toEqual([]);
  });

  it('changes when a C# DTO changes — the basis of the api:check drift guard', () => {
    const before = serializeSpec(
      buildSpec({ repoRoot: repo({ ...FRAMEWORK, ...TIERS }), overrides: NO_OVERRIDES }),
    );
    rmSync(root!, { recursive: true, force: true });
    const renamed = {
      ...TIERS,
      'src/dEngage.Loyalty.Api/Tiers/TiersDtos.cs': TIERS[
        'src/dEngage.Loyalty.Api/Tiers/TiersDtos.cs'
      ].replace('decimal MinPoints, int?', 'decimal MinimumPoints, int?'),
    };
    const after = serializeSpec(
      buildSpec({ repoRoot: repo({ ...FRAMEWORK, ...renamed }), overrides: NO_OVERRIDES }),
    );
    expect(after).not.toBe(before);
    expect(after).toContain('"minimumPoints"');
  });

  it('applies string-union overrides, and fails on one that matches no C# member', () => {
    const files = repo({ ...FRAMEWORK, ...TIERS });
    const spec = buildSpec({
      repoRoot: files,
      overrides: {
        excludeModules: [],
        stringUnions: {
          'TierResponse.rounding': { values: ['down', 'up'], source: 'test' },
          'TierResponse.tags': { values: ['a', 'b'], source: 'test' },
        },
      },
    }) as Json;
    expect(spec.components.schemas.TierResponse.properties.rounding).toEqual({
      type: 'string',
      enum: ['down', 'up'],
    });
    // A list member keeps its array: each item is one of the values.
    expect(spec.components.schemas.TierResponse.properties.tags).toEqual({
      type: 'array',
      items: { type: 'string', enum: ['a', 'b'] },
    });
    expect(() =>
      buildSpec({
        repoRoot: files,
        overrides: {
          excludeModules: [],
          stringUnions: { 'TierResponse.gone': { values: ['x'], source: 'test' } },
        },
      }),
    ).toThrow(/match no C# member/);
  });

  it('fails loudly on a type it cannot find instead of guessing', () => {
    const broken = {
      ...TIERS,
      'src/dEngage.Loyalty.Api/Tiers/TiersDtos.cs': TIERS[
        'src/dEngage.Loyalty.Api/Tiers/TiersDtos.cs'
      ].replace('RuleCalculation? Calculation', 'MysteryType? Calculation'),
    };
    expect(() => buildSpec({ repoRoot: repo({ ...FRAMEWORK, ...broken }), overrides: NO_OVERRIDES })).toThrow(
      /Unknown type 'MysteryType'/,
    );
  });
});
