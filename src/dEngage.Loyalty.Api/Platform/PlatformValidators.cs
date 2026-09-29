using FluentValidation;
using dEngage.Loyalty.Shared;

namespace dEngage.Loyalty.Api.Platform;

public sealed class CreateTenantRequestValidator : AbstractValidator<CreateTenantRequest>
{
    public CreateTenantRequestValidator()
    {
        // The id is interpolated into partition DDL (see PlatformAppService) — validated by shape
        // here, not just parameterization, since DDL identifiers can't be parameterized at all.
        RuleFor(x => x.Id).Matches("^[a-z0-9_]{1,50}$")
            .WithMessage("Tenant id must be 1-50 lowercase letters, digits, or underscores.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
    }
}

public sealed class UpdateTenantRequestValidator : AbstractValidator<UpdateTenantRequest>
{
    public UpdateTenantRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(255).When(x => x.Name is not null);
        RuleFor(x => x.Status).Must(s => s is TenantStatus.Active or TenantStatus.Suspended)
            .When(x => x.Status is not null)
            .WithMessage("Status must be 'active' or 'suspended'.");
    }
}

public sealed class CreateAdminUserRequestValidator : AbstractValidator<CreateAdminUserRequest>
{
    public CreateAdminUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(255);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(12);
        RuleFor(x => x.Role).Must(r => r is "platform_admin" or "tenant_admin")
            .WithMessage("Role must be 'platform_admin' or 'tenant_admin'.");
    }
}
