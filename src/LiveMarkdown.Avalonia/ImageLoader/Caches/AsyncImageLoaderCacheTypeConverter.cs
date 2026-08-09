using System.ComponentModel;
using System.Globalization;

namespace LiveMarkdown.Avalonia;

#if NETSTANDARD2_0
/// <summary>
/// Converts XAML string values into built-in <see cref="AsyncImageLoaderCache"/> instances.
/// </summary>
public class AsyncImageLoaderCacheTypeConverter : TypeConverter
{
    /// <inheritdoc/>
    public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
    {
        return sourceType == typeof(string);
    }

    /// <inheritdoc/>
    public override object? ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object? value)
    {
        if (value is string str)
        {
            return str switch
            {
                "None" => null,
                "Ram" => RamBasedAsyncImageLoaderCache.Shared,
                "File" => FileBasedAsyncImageLoaderCache.Shared,
                "Disk" => FileBasedAsyncImageLoaderCache.Shared,
                _ => throw new NotSupportedException($"Cache type '{str}' is not supported.")
            };
        }

        return base.ConvertFrom(context, culture, value);
    }
}
#else
/// <summary>
/// Converts XAML string values into built-in <see cref="AsyncImageLoaderCache"/> instances.
/// </summary>
public class AsyncImageLoaderCacheTypeConverter : TypeConverter
{
    /// <inheritdoc/>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string);
    }

    /// <inheritdoc/>
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string str)
        {
            return str switch
            {
                "None" => null,
                "Ram" => RamBasedAsyncImageLoaderCache.Shared,
                "File" => FileBasedAsyncImageLoaderCache.Shared,
                _ => throw new NotSupportedException($"Cache type '{str}' is not supported.")
            };
        }

        return base.ConvertFrom(context, culture, value);
    }
}
#endif
