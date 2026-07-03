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

        var buildIndex = normalized.IndexOf('+', StringComparison.Ordinal);
        if (buildIndex >= 0)
        {
            normalized = normalized[..buildIndex];
        }

        var prereleaseIdentifiers = Array.Empty<string>();
        var prereleaseIndex = normalized.IndexOf('-', StringComparison.Ordinal);
        if (prereleaseIndex >= 0)
        {
            var prerelease = normalized[(prereleaseIndex + 1)..];
            normalized = normalized[..prereleaseIndex];
            prereleaseIdentifiers = prerelease.Split('.', StringSplitOptions.TrimEntries);
            if (prereleaseIdentifiers.Length == 0 || prereleaseIdentifiers.Any(string.IsNullOrWhiteSpace))
            {
                return false;
            }
        }

        var parts = normalized.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 4)
        {
            return false;
        }

        var numbers = new int[4];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], out var parsed) || parsed < 0)
            {
                return false;
            }

            numbers[index] = parsed;
        }

        if (parts.Length == 4 && numbers[3] != 0)
        {
            return false;
        }

        version = new SemanticVersionInfo(numbers[0], numbers[1], numbers[2], prereleaseIdentifiers);
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
            var leftIsNumber = int.TryParse(left, out var leftNumber);
            var rightIsNumber = int.TryParse(right, out var rightNumber);

            if (leftIsNumber && rightIsNumber)
            {
                var numeric = leftNumber.CompareTo(rightNumber);
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
}
