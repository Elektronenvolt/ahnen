using Ahnen.Core.Models;

namespace Ahnen.Core.Services;

public enum RelationshipKind
{
    None, Self, Spouse, Ancestor, Descendant, Sibling, HalfSibling,
    UncleAunt, NephewNiece, Cousin, ParentInLaw, ChildInLaw, SiblingInLaw,
    StepParent, StepChild, Relative
}

/// <summary>
/// Structured result of a relationship derivation. <see cref="Degree"/> means:
/// generation distance for Ancestor/Descendant/UncleAunt/NephewNiece
/// (1 = parent/child/uncle/nephew, 2 = grandparent/…), cousin degree for Cousin.
/// </summary>
public record RelationshipResult(RelationshipKind Kind, int Degree = 0, int Removed = 0, Sex TargetSex = Sex.U,
    FamilyEventType? SpouseStatus = null)
{
    public static readonly RelationshipResult None = new(RelationshipKind.None);
}

/// <summary>
/// Derives the relationship between two persons from the structural links
/// (partner, parent-child) — brother, nephew, cousin etc. are never stored.
/// </summary>
public class RelationshipService(DataStore store)
{
    private enum Edge { Up, Down, Partner }

    /// <summary>Finds the shortest structural path from <paramref name="fromId"/> to <paramref name="toId"/> and classifies it.</summary>
    public RelationshipResult Describe(string fromId, string toId, int maxDepth = 12)
    {
        if (fromId == toId)
            return new RelationshipResult(RelationshipKind.Self, TargetSex: store.GetPerson(toId)?.Sex ?? Sex.U);

        var path = FindPath(fromId, toId, maxDepth);
        if (path is null) return RelationshipResult.None;
        return Classify(path, fromId, toId);
    }

    private List<(Edge Edge, string To)>? FindPath(string fromId, string toId, int maxDepth)
    {
        var graph = BuildGraph(store.Families);
        var cameFrom = new Dictionary<string, (string From, Edge Edge, int Depth)> { [fromId] = (fromId, Edge.Partner, 0) };
        var queue = new Queue<string>([fromId]);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            var depth = cameFrom[id].Depth;
            if (depth >= maxDepth || !graph.TryGetValue(id, out var neighbors)) continue;
            foreach (var (edge, next) in neighbors)
            {
                if (cameFrom.ContainsKey(next)) continue;
                cameFrom[next] = (id, edge, depth + 1);
                if (next == toId) return PathTo(cameFrom, fromId, toId);
                queue.Enqueue(next);
            }
        }
        return null;
    }

    private static List<(Edge Edge, string To)> PathTo(
        Dictionary<string, (string From, Edge Edge, int Depth)> cameFrom, string fromId, string toId)
    {
        var path = new List<(Edge, string)>();
        for (var id = toId; id != fromId; id = cameFrom[id].From)
            path.Add((cameFrom[id].Edge, id));
        path.Reverse();
        return path;
    }

    /// <summary>Neighbours of every person, parents first, then children, then partners.</summary>
    private static Dictionary<string, List<(Edge, string)>> BuildGraph(IReadOnlyList<Family> families)
    {
        var parents = new Dictionary<string, List<(Edge, string)>>();
        var children = new Dictionary<string, List<(Edge, string)>>();
        var partners = new Dictionary<string, List<(Edge, string)>>();
        static void Add(Dictionary<string, List<(Edge, string)>> map, string from, Edge edge, string to)
        {
            if (!map.TryGetValue(from, out var list)) map[from] = list = [];
            if (!list.Contains((edge, to))) list.Add((edge, to));
        }

        foreach (var family in families)
            foreach (var partner in family.PartnerIds)
            {
                foreach (var other in family.PartnerIds.Where(x => x != partner))
                    Add(partners, partner, Edge.Partner, other);
                foreach (var child in family.ChildRefs.Select(c => c.PersonId))
                {
                    Add(children, partner, Edge.Down, child);
                    Add(parents, child, Edge.Up, partner);
                }
            }

        var graph = new Dictionary<string, List<(Edge, string)>>();
        foreach (var map in new[] { parents, children, partners })
            foreach (var (id, list) in map)
            {
                if (!graph.TryGetValue(id, out var all)) graph[id] = all = [];
                all.AddRange(list);
            }
        return graph;
    }

    private RelationshipResult Classify(List<(Edge Edge, string To)> path, string fromId, string toId)
    {
        var sex = store.GetPerson(toId)?.Sex ?? Sex.U;
        var edges = path.Select(p => p.Edge).ToList();

        // Direct spouse
        if (edges is [Edge.Partner])
        {
            var family = store.FamiliesAsPartner(fromId)
                .FirstOrDefault(f => f.PartnerIds.Contains(toId));
            return new RelationshipResult(RelationshipKind.Spouse, TargetSex: sex,
                SpouseStatus: family?.LatestStatusEvent?.Type);
        }

        // Pure Up*m Down*n core
        var ups = edges.TakeWhile(e => e == Edge.Up).Count();
        var downs = edges.Skip(ups).TakeWhile(e => e == Edge.Down).Count();
        if (ups + downs == edges.Count)
        {
            if (downs == 0) return new RelationshipResult(RelationshipKind.Ancestor, ups, TargetSex: sex);
            if (ups == 0) return new RelationshipResult(RelationshipKind.Descendant, downs, TargetSex: sex);
            if (ups == 1 && downs == 1)
            {
                var shared = store.ParentsOf(fromId).Select(p => p.Id)
                    .Intersect(store.ParentsOf(toId).Select(p => p.Id)).Count();
                return new RelationshipResult(shared >= 2 ? RelationshipKind.Sibling : RelationshipKind.HalfSibling,
                    TargetSex: sex);
            }
            if (downs == 1) return new RelationshipResult(RelationshipKind.UncleAunt, ups - 2, TargetSex: sex);
            if (ups == 1) return new RelationshipResult(RelationshipKind.NephewNiece, downs - 2, TargetSex: sex);
            return new RelationshipResult(RelationshipKind.Cousin,
                Degree: Math.Min(ups, downs) - 1, Removed: Math.Abs(ups - downs), TargetSex: sex);
        }

        // One partner edge at the start or end → in-law / step relations
        switch (edges)
        {
            case [Edge.Partner, Edge.Up]: return new RelationshipResult(RelationshipKind.ParentInLaw, TargetSex: sex);
            case [Edge.Down, Edge.Partner]: return new RelationshipResult(RelationshipKind.ChildInLaw, TargetSex: sex);
            case [Edge.Up, Edge.Partner]: return new RelationshipResult(RelationshipKind.StepParent, TargetSex: sex);
            case [Edge.Partner, Edge.Down]: return new RelationshipResult(RelationshipKind.StepChild, TargetSex: sex);
            case [Edge.Up, Edge.Down, Edge.Partner]:
            case [Edge.Partner, Edge.Up, Edge.Down]:
                return new RelationshipResult(RelationshipKind.SiblingInLaw, TargetSex: sex);
        }

        return new RelationshipResult(RelationshipKind.Relative, TargetSex: sex);
    }
}
