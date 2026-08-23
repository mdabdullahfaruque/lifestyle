using Lifestyle.SharedKernel.Domain;
using Lifestyle.SharedKernel.Results;

namespace Lifestyle.Modules.Catalog.Domain;

/// <summary>
/// The attributes that apply to a category (FRD §7.2). "Dresses" has size, colour, material;
/// "Mobile accessories" has compatibility and connector type. Data, not code — adding a category
/// is configuration (Plan §1).
/// </summary>
internal sealed class AttributeSet : AggregateRoot
{
    private readonly List<ProductAttribute> _attributes = [];

    private AttributeSet() { }

    public string Name { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public string? Description { get; private set; }

    public IReadOnlyCollection<ProductAttribute> Attributes => _attributes.AsReadOnly();

    /// <summary>The attributes that generate variants — size and colour, not material.</summary>
    public IEnumerable<ProductAttribute> VariantAttributes => _attributes.Where(a => a.IsVariantAxis);

    public static AttributeSet Create(string name, string code, string? description, DateTimeOffset now) => new()
    {
        Name = name.Trim(),
        Code = code.Trim().ToLowerInvariant(),
        Description = description?.Trim(),
        CreatedAt = now
    };

    public Result AddAttribute(ProductAttribute attribute)
    {
        if (_attributes.Any(a => string.Equals(a.Code, attribute.Code, StringComparison.OrdinalIgnoreCase)))
            return Error.Conflict("catalog.attribute_duplicate", $"'{attribute.Code}' is already in this set.");

        _attributes.Add(attribute);
        return Result.Success();
    }

    public void RemoveAttribute(Guid attributeId) => _attributes.RemoveAll(a => a.Id == attributeId);

    public ProductAttribute? Find(string code) =>
        _attributes.FirstOrDefault(a => string.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One attribute definition. Belongs to exactly one <see cref="AttributeSet"/>.</summary>
internal sealed class ProductAttribute : Entity
{
    private ProductAttribute() { }

    public Guid AttributeSetId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public AttributeDataType DataType { get; private set; }
    public bool IsRequired { get; private set; }

    /// <summary>
    /// True when this attribute's values multiply out into SKUs. Size × Colour gives one variant
    /// per combination; Material does not vary by SKU and is a plain product attribute.
    /// </summary>
    public bool IsVariantAxis { get; private set; }

    /// <summary>True when buyers can filter on it in search.</summary>
    public bool IsFilterable { get; private set; }

    public int SortOrder { get; private set; }
    public string? Unit { get; private set; }

    /// <summary>Allowed values for <see cref="AttributeDataType.Select"/>. Empty otherwise.</summary>
    public List<string> AllowedValues { get; private set; } = [];

    public static ProductAttribute Create(
        string name, string code, AttributeDataType dataType, bool isRequired, bool isVariantAxis,
        bool isFilterable, int sortOrder, string? unit = null, IEnumerable<string>? allowedValues = null) => new()
        {
            Name = name.Trim(),
            Code = code.Trim().ToLowerInvariant(),
            DataType = dataType,
            IsRequired = isRequired,
            IsVariantAxis = isVariantAxis,
            IsFilterable = isFilterable,
            SortOrder = sortOrder,
            Unit = unit,
            AllowedValues = [.. (allowedValues ?? []).Distinct(StringComparer.OrdinalIgnoreCase)]
        };

    /// <summary>Checks one supplied value against this attribute's type and allow-list.</summary>
    public Result ValidateValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return IsRequired
                ? Error.Validation($"catalog.attribute_required.{Code}", $"{Name} is required.")
                : Result.Success();

        return DataType switch
        {
            AttributeDataType.Select when !AllowedValues.Contains(value, StringComparer.OrdinalIgnoreCase) =>
                Error.Validation($"catalog.attribute_invalid.{Code}",
                    $"{Name} must be one of: {string.Join(", ", AllowedValues)}."),

            AttributeDataType.Number when !decimal.TryParse(value,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out _) =>
                Error.Validation($"catalog.attribute_invalid.{Code}", $"{Name} must be a number."),

            AttributeDataType.Boolean when !bool.TryParse(value, out _) =>
                Error.Validation($"catalog.attribute_invalid.{Code}", $"{Name} must be true or false."),

            _ => Result.Success()
        };
    }
}

internal enum AttributeDataType
{
    Text = 1,

    /// <summary>One of a fixed list. The only type that can be a variant axis.</summary>
    Select = 2,
    Number = 3,
    Boolean = 4
}
