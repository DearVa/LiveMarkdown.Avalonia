using System.Globalization;
using System.Text.Json.Serialization;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Metadata associated with cached image bytes.
/// </summary>
/// <remarks>
/// The strongly typed properties describe generic cache state. Protocol-specific
/// values, such as HTTP validators, are stored in <see cref="Properties"/>.
/// </remarks>
public class AsyncImageLoaderCacheMetadata
{
    /// <summary>
    /// Property key used for an HTTP ETag validator.
    /// </summary>
    public const string HttpETagProperty = "http.etag";

    /// <summary>
    /// Property key used for an HTTP Last-Modified validator.
    /// </summary>
    public const string HttpLastModifiedProperty = "http.lastModified";

    /// <summary>
    /// Property key used for the HTTP Cache-Control header value.
    /// </summary>
    public const string HttpCacheControlProperty = "http.cacheControl";

    /// <summary>
    /// Property key used for the response content type.
    /// </summary>
    public const string ContentTypeProperty = "content.type";

    /// <summary>
    /// Gets or sets the normalized source URI string that produced the cached bytes.
    /// </summary>
    public string? SourceUri { get; set; }

    /// <summary>
    /// Gets or sets when this cache entry was first created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets when this cache entry was last written.
    /// </summary>
    public DateTimeOffset StoredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets when this cache entry was last read.
    /// </summary>
    public DateTimeOffset LastAccessedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets when this cache entry becomes stale. A null value means the
    /// entry has no explicit expiration time.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets the cached byte length, when known.
    /// </summary>
    public long? Length { get; set; }

    /// <summary>
    /// Gets or sets a lowercase hexadecimal SHA-256 hash of the cached bytes.
    /// </summary>
    public string? BodyHash { get; set; }

    /// <summary>
    /// Gets or sets whether this response must not be stored in a cache.
    /// </summary>
    public bool NoStore { get; set; }

    /// <summary>
    /// Gets or sets whether this response must be revalidated before use.
    /// </summary>
    public bool NoCache { get; set; }

    /// <summary>
    /// Gets protocol-specific or application-specific metadata values.
    /// </summary>
    public Dictionary<string, string?> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets whether this entry has an HTTP validator stored in <see cref="Properties"/>.
    /// </summary>
    public bool HasHttpValidators => !string.IsNullOrWhiteSpace(GetHttpETag()) || GetHttpLastModified() is not null;

    /// <summary>
    /// Determines whether the cache entry is fresh at the supplied time.
    /// </summary>
    /// <param name="now">The current time to compare against <see cref="ExpiresAt"/>.</param>
    /// <returns>True when the entry can be used without revalidation; otherwise false.</returns>
    public bool IsFresh(DateTimeOffset now) =>
        !NoCache && (!ExpiresAt.HasValue || ExpiresAt.Value > now);

    /// <summary>
    /// Creates metadata for a source URI using the current UTC time.
    /// </summary>
    /// <param name="uri">The source URI associated with the cached bytes.</param>
    /// <returns>A new metadata instance.</returns>
    public static AsyncImageLoaderCacheMetadata Create(Uri uri)
    {
        var now = DateTimeOffset.UtcNow;
        return new AsyncImageLoaderCacheMetadata
        {
            SourceUri = uri.OriginalString,
            CreatedAt = now,
            StoredAt = now,
            LastAccessedAt = now
        };
    }

    /// <summary>
    /// Creates a deep copy of this metadata instance.
    /// </summary>
    /// <returns>A metadata copy with an independent <see cref="Properties"/> dictionary.</returns>
    public AsyncImageLoaderCacheMetadata Clone() =>
        new()
        {
            SourceUri = SourceUri,
            CreatedAt = CreatedAt,
            StoredAt = StoredAt,
            LastAccessedAt = LastAccessedAt,
            ExpiresAt = ExpiresAt,
            Length = Length,
            BodyHash = BodyHash,
            NoStore = NoStore,
            NoCache = NoCache,
            Properties = new Dictionary<string, string?>(Properties, StringComparer.OrdinalIgnoreCase)
        };

    /// <summary>
    /// Merges metadata returned from a validation request into this cached entry.
    /// </summary>
    /// <param name="metadata">The metadata returned by the validator request.</param>
    public void UpdateFromValidation(AsyncImageLoaderCacheMetadata metadata)
    {
        if (metadata.ExpiresAt is not null) ExpiresAt = metadata.ExpiresAt;

        NoStore = metadata.NoStore;
        NoCache = metadata.NoCache;
        LastAccessedAt = DateTimeOffset.UtcNow;

        foreach (var (key, value) in metadata.Properties)
        {
            SetProperty(key, value);
        }
    }

    /// <summary>
    /// Gets an extension property value.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <returns>The stored value, or null when the key is absent.</returns>
    public string? GetProperty(string key) =>
#if NETSTANDARD2_0
        Properties.TryGetValue(key, out var value) ? value : null;
#else
        Properties.GetValueOrDefault(key);
#endif

    /// <summary>
    /// Sets or removes an extension property value.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <param name="value">The value to store. Null removes the key.</param>
    public void SetProperty(string key, string? value)
    {
        if (value is null)
        {
            Properties.Remove(key);
            return;
        }

        Properties[key] = value;
    }

    /// <summary>
    /// Gets the stored HTTP ETag validator.
    /// </summary>
    /// <returns>The ETag value, or null when it is absent.</returns>
    public string? GetHttpETag() => GetProperty(HttpETagProperty);

    /// <summary>
    /// Stores the HTTP ETag validator.
    /// </summary>
    /// <param name="value">The ETag value, or null to remove it.</param>
    public void SetHttpETag(string? value) => SetProperty(HttpETagProperty, value);

    /// <summary>
    /// Gets the stored HTTP Last-Modified validator.
    /// </summary>
    /// <returns>The parsed Last-Modified value, or null when it is absent or invalid.</returns>
    public DateTimeOffset? GetHttpLastModified() =>
        DateTimeOffset.TryParse(GetProperty(HttpLastModifiedProperty), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value) ?
            value :
            null;

    /// <summary>
    /// Stores the HTTP Last-Modified validator.
    /// </summary>
    /// <param name="value">The Last-Modified value, or null to remove it.</param>
    public void SetHttpLastModified(DateTimeOffset? value) =>
        SetProperty(HttpLastModifiedProperty, value?.ToString("O", CultureInfo.InvariantCulture));

    /// <summary>
    /// Gets the stored HTTP Cache-Control header value.
    /// </summary>
    /// <returns>The Cache-Control value, or null when it is absent.</returns>
    public string? GetHttpCacheControl() => GetProperty(HttpCacheControlProperty);

    /// <summary>
    /// Stores the HTTP Cache-Control header value.
    /// </summary>
    /// <param name="value">The Cache-Control value, or null to remove it.</param>
    public void SetHttpCacheControl(string? value) => SetProperty(HttpCacheControlProperty, value);

    /// <summary>
    /// Gets the stored content type.
    /// </summary>
    /// <returns>The content type value, or null when it is absent.</returns>
    public string? GetContentType() => GetProperty(ContentTypeProperty);

    /// <summary>
    /// Stores the content type.
    /// </summary>
    /// <param name="value">The content type value, or null to remove it.</param>
    public void SetContentType(string? value) => SetProperty(ContentTypeProperty, value);
}

/// <summary>
/// Provides source-generated JSON metadata for <see cref="AsyncImageLoaderCacheMetadata"/>.
/// </summary>
[JsonSerializable(typeof(AsyncImageLoaderCacheMetadata))]
public sealed partial class AsyncImageLoaderCacheMetadataJsonSerializerContext : JsonSerializerContext;