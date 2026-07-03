using System.Globalization;

namespace Relaywright.Web.Services.Updates;

public sealed class SemanticVersionInfo : IComparable<SemanticVersionInfo>
{
    private SemanticVersionInfo(int major, int minor, int patch, IReadOnlyList<string> prereleaseIdentifiers)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PrereleaseIdentifiers = prereleaseIdentifiers;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public IReadOnlyList<string> PrereleaseIdentifiers { get; }

    public bool IsPrerelease => PrereleaseIdentifiers.Count > 0;

    public static bool TryParse(string? value, out SemanticVersionInfo? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        if (!TryRemoveBuildMetadata(ref normalized)
            || !TryRemovePrerelease(ref normalized, out var prereleaseIdentifiers)
            || !TryParseCoreVersion(normalized, out var major, out var minor, out var patch))
        {
            return false;
        }

        version = new SemanticVersionInfo(major, minor, patch, prereleaseIdentifiers);
        return true;
    }

    public int CompareTo(SemanticVersionInfo? other)
    {
        if (other is null)
        {
            return 1;
        }

        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        if (minor != 0)
        {
            return minor;
        }

        var patch = Patch.CompareTo(other.Patch);
        if (patch != 0)
        {
            return patch;
        }

        if (!IsPrerelease && !other.IsPrerelease)
        {
            return 0;
        }

        if (!IsPrerelease)
        {
            return 1;
        }

        if (!other.IsPrerelease)
        {
            return -1;
        }

        var count = Math.Min(PrereleaseIdentifiers.Count, other.PrereleaseIdentifiers.Count);
        for (var index = 0; index < count; index++)
        {
            var left = PrereleaseIdentifiers[index];
            var right = other.PrereleaseIdentifiers[index];
            var leftIsNumber = IsNumericIdentifier(left);
            var rightIsNumber = IsNumericIdentifier(right);

            if (leftIsNumber && rightIsNumber)
            {
                var numeric = CompareNumericIdentifiers(left, right);
                if (numeric != 0)
                {
                    return numeric;
                }

                continue;
            }

            if (leftIsNumber)
            {
                return -1;
            }

            if (rightIsNumber)
            {
                return 1;
            }

            var lexical = string.CompareOrdinal(left, right);
            if (lexical != 0)
            {
                return lexical;
            }
        }

        return PrereleaseIdentifiers.Count.CompareTo(other.PrereleaseIdentifiers.Count);
    }

    public override string ToString()
    {
        var core = $"{Major}.{Minor}.{Patch}";
        return IsPrerelease
            ? $"{core}-{string.Join('.', PrereleaseIdentifiers)}"
            : core;
    }

    private static bool TryRemoveBuildMetadata(ref string versionText)
    {
        var buildIndex = versionText.IndexOf('+', StringComparison.Ordinal);
        if (buildIndex < 0)
        {
            return true;
        }

        var buildMetadata = versionText[(buildIndex + 1)..];
        if (!AreDotSeparatedIdentifiersValid(buildMetadata, allowLeadingZeroNumericIdentifiers: true))
        {
            return false;
        }

        versionText = versionText[..buildIndex];
        return true;
    }

    private static bool TryRemovePrerelease(ref string versionText, out IReadOnlyList<string> prereleaseIdentifiers)
    {
        prereleaseIdentifiers = Array.Empty<string>();
        var prereleaseIndex = versionText.IndexOf('-', StringComparison.Ordinal);
        if (prereleaseIndex < 0)
        {
            return true;
        }

        var prerelease = versionText[(prereleaseIndex + 1)..];
        if (!AreDotSeparatedIdentifiersValid(prerelease, allowLeadingZeroNumericIdentifiers: false))
        {
            return false;
        }

        prereleaseIdentifiers = prerelease.Split('.', StringSplitOptions.None);
        versionText = versionText[..prereleaseIndex];
        return true;
    }

    private static bool TryParseCoreVersion(string versionText, out int major, out int minor, out int patch)
    {
        major = 0;
        minor = 0;
        patch = 0;

        var parts = versionText.Split('.', StringSplitOptions.None);
        if (parts.Length is not (3 or 4))
        {
            return false;
        }

        if (!TryParseCoreNumber(parts[0], out major)
            || !TryParseCoreNumber(parts[1], out minor)
            || !TryParseCoreNumber(parts[2], out patch))
        {
            return false;
        }

        return parts.Length == 3
            || (TryParseCoreNumber(parts[3], out var revision) && revision == 0);
    }

    private static bool TryParseCoreNumber(string value, out int number)
    {
        number = 0;
        return IsNumericIdentifier(value)
            && !HasLeadingZero(value)
            && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    private static bool AreDotSeparatedIdentifiersValid(
        string value,
        bool allowLeadingZeroNumericIdentifiers)
    {
        var identifiers = value.Split('.', StringSplitOptions.None);
        return identifiers.All(identifier => IsIdentifierValid(identifier, allowLeadingZeroNumericIdentifiers));
    }

    private static bool IsIdentifierValid(string identifier, bool allowLeadingZeroNumericIdentifiers)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return false;
        }

        foreach (var character in identifier)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-')
            {
                return false;
            }
        }

        return allowLeadingZeroNumericIdentifiers
            || !IsNumericIdentifier(identifier)
            || !HasLeadingZero(identifier);
    }

    private static bool IsNumericIdentifier(string identifier)
    {
        return identifier.Length > 0 && identifier.All(char.IsAsciiDigit);
    }

    private static bool HasLeadingZero(string identifier)
    {
        return identifier.Length > 1 && identifier[0] == '0';
    }

    private static int CompareNumericIdentifiers(string left, string right)
    {
        var length = left.Length.CompareTo(right.Length);
        return length != 0
            ? length
            : string.CompareOrdinal(left, right);
    }
}
