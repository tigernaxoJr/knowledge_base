namespace Assistant.Core.Search;

public sealed class RoutingDecision : IRoutingDecision
{
    public (RoutingAction Action, SearchResult? BestMatch) Decide(IReadOnlyList<SearchResult> candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return (RoutingAction.CreateNew, null);
        }

        // SearchResults are ordered by Score descending. The first candidate is the best match.
        var bestMatch = candidates[0];

        if (bestMatch.Score >= RoutingThresholds.MergeThreshold)
        {
            return (RoutingAction.Merge, bestMatch);
        }

        return (RoutingAction.CreateNew, null);
    }
}
