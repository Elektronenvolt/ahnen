using Ahnen.Core.Models;
using Ahnen.Core.Services;

namespace Ahnen.Desktop.Visualization;

public enum TreeChart { Hourglass, Relatives, Ancestors, Descendants }
public enum TreeColors { Sex, Generation, None }
public enum TreeOrientation { Vertical, Horizontal }
public enum TreeLinkStyle { Married, Ended, Unmarried, Adopted }

public sealed record TreeLayoutOptions(string? FocusId, TreeChart Chart = TreeChart.Hourglass,
    int Generations = 4, bool Overview = false, TreeOrientation Orientation = TreeOrientation.Vertical,
    IReadOnlySet<string>? Collapsed = null);

public readonly record struct ChartPoint(double X, double Y);
public readonly record struct ChartBounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public ChartPoint Center => new(X + Width / 2, Y + Height / 2);
    public bool Contains(ChartPoint p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
    public bool Intersects(ChartBounds other) => X <= other.Right && Right >= other.X && Y <= other.Bottom && Bottom >= other.Y;
}

public sealed record TreeCard(Person Person, ChartBounds Bounds, int Generation, bool Collapsed);
public sealed record TreeLink(string FamilyId, string PersonId, TreeLinkStyle Style, IReadOnlyList<ChartPoint> Points);
public sealed record TreeJunction(string FamilyId, ChartPoint Position, string Status);

/// <summary>Renderer-independent scene with a uniform grid for bounded card picking and viewport queries.</summary>
public sealed class TreeScene
{
    private const double CellSize = 400;
    private readonly Dictionary<(int X, int Y), List<TreeCard>> _cells = [];
    public IReadOnlyList<TreeCard> Cards { get; }
    public IReadOnlyList<TreeLink> Links { get; }
    public IReadOnlyList<TreeJunction> Junctions { get; }
    public ChartBounds Bounds { get; }
    public int GenerationConflicts { get; }

    public TreeScene(IReadOnlyList<TreeCard> cards, IReadOnlyList<TreeLink> links,
        IReadOnlyList<TreeJunction> junctions, int generationConflicts)
    {
        Cards = cards;
        Links = links;
        Junctions = junctions;
        GenerationConflicts = generationConflicts;
        Bounds = cards.Count == 0 ? new(0, 0, 1, 1) : new(0, 0,
            cards.Max(c => c.Bounds.Right) + 60, cards.Max(c => c.Bounds.Bottom) + 100);
        foreach (var card in cards)
            foreach (var cell in Cells(card.Bounds))
            {
                if (!_cells.TryGetValue(cell, out var list)) _cells[cell] = list = [];
                list.Add(card);
            }
    }

    public TreeCard? HitTest(ChartPoint point) =>
        _cells.GetValueOrDefault(((int)Math.Floor(point.X / CellSize), (int)Math.Floor(point.Y / CellSize)))
            ?.FirstOrDefault(c => c.Bounds.Contains(point));

    public IEnumerable<TreeCard> InViewport(ChartBounds viewport)
    {
        // At overview scales enumerating occupied cells is cheaper than traversing an enormous empty grid.
        if (viewport.Width * viewport.Height > _cells.Count * CellSize * CellSize)
            return Cards.Where(c => c.Bounds.Intersects(viewport));
        return Cells(viewport).Where(_cells.ContainsKey).SelectMany(c => _cells[c])
            .Distinct().Where(c => c.Bounds.Intersects(viewport));
    }

    private static IEnumerable<(int, int)> Cells(ChartBounds bounds)
    {
        for (var x = (int)Math.Floor(bounds.X / CellSize); x <= (int)Math.Floor(bounds.Right / CellSize); x++)
            for (var y = (int)Math.Floor(bounds.Y / CellSize); y <= (int)Math.Floor(bounds.Bottom / CellSize); y++)
                yield return (x, y);
    }
}

public static class TreeLayout
{
    private sealed record Edge(string To, int Delta);

    public static TreeScene Build(FamilyTreeData data, TreeLayoutOptions options, CancellationToken cancellationToken = default)
    {
        var people = data.Persons.ToDictionary(p => p.Id);
        var graph = people.Keys.ToDictionary(id => id, _ => new List<Edge>());
        foreach (var family in data.Families)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var partners = family.PartnerIds.Where(people.ContainsKey).Distinct().ToArray();
            foreach (var partner in partners)
            {
                foreach (var other in partners.Where(id => id != partner))
                    graph[partner].Add(new(other, 0));
                foreach (var child in family.ChildRefs.Where(c => people.ContainsKey(c.PersonId)))
                {
                    graph[partner].Add(new(child.PersonId, 1));
                    graph[child.PersonId].Add(new(partner, -1));
                }
            }
            // Parentless families still connect their siblings in the overview.
            if (partners.Length == 0)
            {
                var children = family.ChildRefs.Select(c => c.PersonId).Where(people.ContainsKey).Distinct().ToArray();
                foreach (var child in children.Skip(1))
                {
                    graph[children[0]].Add(new(child, 0));
                    graph[child].Add(new(children[0], 0));
                }
            }
        }

        var focus = options.FocusId is { } id && people.ContainsKey(id) ? id
            : data.RootPersonId is { } root && people.ContainsKey(root) ? root : people.Keys.FirstOrDefault();
        if (focus is null) return new([], [], [], 0);

        var levels = new Dictionary<string, int>();
        var components = new Dictionary<string, int>();
        var conflicts = new HashSet<(string, string)>();
        var component = 0;
        foreach (var start in new[] { data.RootPersonId, focus }.Where(id => id is not null).Cast<string>()
                     .Concat(people.Keys.Order(StringComparer.Ordinal)).Distinct())
        {
            if (!people.ContainsKey(start) || levels.ContainsKey(start)) continue;
            levels[start] = 0;
            components[start] = component;
            var queue = new Queue<string>([start]);
            while (queue.TryDequeue(out var current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var edge in graph[current])
                {
                    var level = levels[current] + edge.Delta;
                    if (levels.TryAdd(edge.To, level))
                    {
                        components[edge.To] = component;
                        queue.Enqueue(edge.To);
                    }
                    else if (levels[edge.To] != level)
                        conflicts.Add(string.CompareOrdinal(current, edge.To) < 0 ? (current, edge.To) : (edge.To, current));
                }
            }
            component++;
        }

        var visible = options.Overview ? people.Keys.ToHashSet() : Select(graph, focus, options, cancellationToken);
        var rows = visible.GroupBy(id => (Component: components[id], Level: levels[id]))
            .OrderBy(g => g.Key.Component).ThenBy(g => g.Key.Level).ToArray();
        var cards = new List<TreeCard>();
        var offset = 60.0;
        foreach (var group in rows.GroupBy(g => g.Key.Component))
        {
            var minLevel = group.Min(g => g.Key.Level);
            var maxSlots = group.Max(g => g.Count());
            foreach (var row in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var slot = 0;
                var remaining = row.ToHashSet();
                // Walk partnership groups together, without duplicating people who married more than once.
                foreach (var seed in row.Order(StringComparer.Ordinal))
                {
                    if (!remaining.Remove(seed)) continue;
                    var queue = new Queue<string>([seed]);
                    while (queue.TryDequeue(out var current))
                    {
                        var along = (row.Key.Level - minLevel) * 180 + 60;
                        var across = offset + slot++ * 290;
                        var bounds = options.Orientation == TreeOrientation.Vertical
                            ? new ChartBounds(across, along, 250, 104)
                            : new ChartBounds(along * 1.8, across * 0.5, 250, 104);
                        cards.Add(new(people[current], bounds, -levels[current], options.Collapsed?.Contains(current) == true));
                        foreach (var partner in graph[current].Where(e => e.Delta == 0).Select(e => e.To))
                            if (remaining.Remove(partner)) queue.Enqueue(partner);
                    }
                }
            }
            offset += maxSlots * 290 + 120;
        }

        var byId = cards.ToDictionary(c => c.Person.Id);
        var links = new List<TreeLink>();
        var junctions = new List<TreeJunction>();
        foreach (var family in data.Families)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parents = family.PartnerIds.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
            var children = family.ChildRefs.Where(c => byId.ContainsKey(c.PersonId)).ToArray();
            if (parents.Length + children.Length == 0) continue;
            var horizontal = options.Orientation == TreeOrientation.Horizontal;
            var members = parents.Concat(children.Select(c => byId[c.PersonId])).ToArray();
            var junction = horizontal
                ? new ChartPoint(parents.Length > 0 ? parents.Max(c => c.Bounds.Right) + 24 : members.Min(c => c.Bounds.X) - 24,
                    members.Average(c => c.Bounds.Center.Y))
                : new ChartPoint(members.Average(c => c.Bounds.Center.X),
                    parents.Length > 0 ? parents.Max(c => c.Bounds.Bottom) + 24 : members.Min(c => c.Bounds.Y) - 24);
            var status = FamilyStatus.GetStatus(family);
            junctions.Add(new(family.Id, junction, status));
            var style = status is "DIV" or "SEP" or "ANUL" or "DIVF" ? TreeLinkStyle.Ended
                : status == "MARR" ? TreeLinkStyle.Married : TreeLinkStyle.Unmarried;
            foreach (var parent in parents)
            {
                var p = horizontal ? new ChartPoint(parent.Bounds.Right, parent.Bounds.Center.Y)
                    : new ChartPoint(parent.Bounds.Center.X, parent.Bounds.Bottom);
                links.Add(new(family.Id, parent.Person.Id, style, Route(p, junction, horizontal)));
            }
            foreach (var child in children)
            {
                var card = byId[child.PersonId];
                var p = horizontal ? new ChartPoint(card.Bounds.X, card.Bounds.Center.Y)
                    : new ChartPoint(card.Bounds.Center.X, card.Bounds.Y);
                links.Add(new(family.Id, child.PersonId,
                    child.Pedigree == Pedigree.BIRTH ? TreeLinkStyle.Married : TreeLinkStyle.Adopted,
                    Route(junction, p, !horizontal)));
            }
        }
        return new(cards, links, junctions, conflicts.Count);
    }

    private static IReadOnlyList<ChartPoint> Route(ChartPoint start, ChartPoint end, bool horizontal) =>
        horizontal ? [start, new(end.X, start.Y), end] : [start, new(start.X, end.Y), end];

    private static HashSet<string> Select(Dictionary<string, List<Edge>> graph, string focus,
        TreeLayoutOptions options, CancellationToken token)
    {
        var selected = new HashSet<string> { focus };
        void Walk(int direction)
        {
            var distances = new Dictionary<string, int> { [focus] = 0 };
            var queue = new Queue<(string Id, int Depth)>([(focus, 0)]);
            while (queue.TryDequeue(out var current))
            {
                token.ThrowIfCancellationRequested();
                if (options.Collapsed?.Contains(current.Id) == true) continue;
                foreach (var edge in graph[current.Id])
                {
                    if (direction != 0 && edge.Delta != 0 && edge.Delta != direction) continue;
                    var depth = current.Depth + (edge.Delta == 0 ? 0 : 1);
                    if (options.Generations > 0 && depth > options.Generations) continue;
                    if (distances.TryGetValue(edge.To, out var previous) && previous <= depth) continue;
                    distances[edge.To] = depth;
                    selected.Add(edge.To);
                    queue.Enqueue((edge.To, depth));
                }
            }
        }
        if (options.Chart == TreeChart.Relatives) Walk(0);
        else
        {
            if (options.Chart != TreeChart.Descendants) Walk(-1);
            if (options.Chart != TreeChart.Ancestors) Walk(1);
        }
        return selected;
    }
}
