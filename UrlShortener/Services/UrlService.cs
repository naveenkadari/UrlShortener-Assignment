using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UrlShortener.Data;
using UrlShortener.Domain;

namespace UrlShortener.Services;

public enum CreateStatus { Created, Conflict, Invalid }

public record CreateResult(CreateStatus Status, ShortUrl? Url = null, string? Error = null);

public class UrlService(AppDb db, TimeProvider clock, IOptions<UrlOptions> options)
{
    private const int MaxAttempts = 5;

    public async Task<CreateResult> CreateAsync(
        string? url, string? alias, DateTime? expiresAt, CancellationToken ct)
    {
        if (!UrlValidator.TryValidateUrl(url, out var error))
            return new(CreateStatus.Invalid, Error: error);

        if (alias is not null && !UrlValidator.TryValidateAlias(alias, out error))
            return new(CreateStatus.Invalid, Error: error);

        var now = clock.GetUtcNow().UtcDateTime;

        if (expiresAt is not null && expiresAt <= now)
            return new(CreateStatus.Invalid, Error: "expiresAt must be in the future");

        var defaultDays = options.Value.DefaultExpiryDays;
        if (expiresAt is null && defaultDays is > 0)
            expiresAt = now.AddDays(defaultDays.Value);

        var attempts = alias is null ? MaxAttempts : 1;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var entity = new ShortUrl
            {
                Code = alias ?? CodeGenerator.Generate(),
                OriginalUrl = url!,
                CreatedAt = now,
                ExpiresAt = expiresAt
            };

            db.Urls.Add(entity);

            try
            {
                await db.SaveChangesAsync(ct);
                return new(CreateStatus.Created, entity);
            }
            catch (DbUpdateException)
            {
                db.Entry(entity).State = EntityState.Detached;

                if (alias is not null)
                    return new(CreateStatus.Conflict, Error: "alias already taken");
            }
        }

        throw new InvalidOperationException("Could not generate a unique code");
    }
}