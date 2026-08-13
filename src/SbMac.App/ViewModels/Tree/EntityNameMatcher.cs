namespace SbMac.App.ViewModels.Tree;

/// <summary>Small, dependency-free fuzzy matcher for Service Bus entity names.</summary>
internal static class EntityNameMatcher
{
    public static string Normalize(string? value) => string.Concat(
        (value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant));

    public static bool IsMatch(string candidate, string normalizedSearchText)
    {
        var normalizedCandidate = Normalize(candidate);
        if (normalizedCandidate.Contains(normalizedSearchText, StringComparison.Ordinal))
        {
            return true;
        }

        var allowedEdits = normalizedSearchText.Length switch
        {
            >= 8 => 2,
            >= 4 => 1,
            _ => 0
        };

        if (allowedEdits == 0)
        {
            return false;
        }

        if (WithinEditDistance(normalizedSearchText, normalizedCandidate, allowedEdits))
        {
            return true;
        }

        return candidate
            .Split(['-', '_', '.', '/', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Any(token => WithinEditDistance(normalizedSearchText, token, allowedEdits));
    }

    static bool WithinEditDistance(string left, string right, int maximum)
    {
        if (Math.Abs(left.Length - right.Length) > maximum)
        {
            return false;
        }

        var distances = new int[left.Length + 1, right.Length + 1];
        for (var leftIndex = 0; leftIndex <= left.Length; leftIndex++)
        {
            distances[leftIndex, 0] = leftIndex;
        }

        for (var rightIndex = 0; rightIndex <= right.Length; rightIndex++)
        {
            distances[0, rightIndex] = rightIndex;
        }

        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitutionCost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                distances[leftIndex, rightIndex] = Math.Min(
                    Math.Min(
                        distances[leftIndex - 1, rightIndex] + 1,
                        distances[leftIndex, rightIndex - 1] + 1),
                    distances[leftIndex - 1, rightIndex - 1] + substitutionCost);

                if (leftIndex > 1 && rightIndex > 1 &&
                    left[leftIndex - 1] == right[rightIndex - 2] &&
                    left[leftIndex - 2] == right[rightIndex - 1])
                {
                    distances[leftIndex, rightIndex] = Math.Min(
                        distances[leftIndex, rightIndex],
                        distances[leftIndex - 2, rightIndex - 2] + 1);
                }
            }
        }

        return distances[left.Length, right.Length] <= maximum;
    }
}
