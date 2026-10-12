namespace FFXILauncher.Core;

public sealed record ArtworkRotationResult(Dictionary<Guid, string> Choices, List<string> Remaining);

public static class ArtworkRotation
{
    public static ArtworkRotationResult Next(IEnumerable<Guid> accounts, IReadOnlyList<string> available,
        IReadOnlyDictionary<Guid, string>? previous, IReadOnlyList<string>? remaining)
    {
        var pool = available.Distinct().ToArray();
        if (pool.Length == 0) throw new ArgumentException("Artwork pool is empty.", nameof(available));
        var queue = remaining == null
            ? pool.Except(previous?.Values ?? []).OrderBy(_ => Random.Shared.Next()).ToList()
            : remaining.Where(pool.Contains).Distinct().ToList();
        var choices = new Dictionary<Guid, string>();
        var batch = new HashSet<string>();
        foreach (var account in accounts)
        {
            if (queue.Count == 0) queue = pool.OrderBy(_ => Random.Shared.Next()).ToList();
            if (batch.Count == pool.Length) batch.Clear();
            var candidates = queue.Where(file => !batch.Contains(file)).ToList();
            if (candidates.Count == 0) throw new InvalidOperationException("Artwork rotation lost its cycle.");
            var last = previous?.GetValueOrDefault(account);
            var choice = candidates.FirstOrDefault(file => file != last) ?? candidates[0];
            queue.Remove(choice);
            choices[account] = choice;
            batch.Add(choice);
        }
        return new(choices, queue);
    }
}
