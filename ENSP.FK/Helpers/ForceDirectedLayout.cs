namespace ENSP.ZD.Helpers;

public class ForceDirectedLayout
{
    private const double RepulsionK = 5000.0;
    private const double AttractionK = 0.01;
    private const double Damping = 0.85;
    private const int MaxIterations = 200;
    private const double ConvergenceThreshold = 0.5;

    public void Layout(List<NodeInfo> nodes, List<LinkInfo> links, double width, double height)
    {
        if (nodes.Count == 0) return;

        // Initialize positions with random perturbation around center
        var rng = new Random(42);
        double cx = width / 2, cy = height / 2;
        foreach (var node in nodes)
        {
            node.X = cx + (rng.NextDouble() - 0.5) * width * 0.4;
            node.Y = cy + (rng.NextDouble() - 0.5) * height * 0.4;
        }

        double temperature = width / 10.0;

        // Build node lookup once (was inside loop — 200× rebuild for 20 nodes = 4000 dict allocs)
        var nodeDict = nodes.ToDictionary(n => n.Id);

        for (int iter = 0; iter < MaxIterations; iter++)
        {
            // Zero velocities
            foreach (var node in nodes)
            {
                node.Vx = 0;
                node.Vy = 0;
            }

            // Repulsion (all node pairs)
            for (int i = 0; i < nodes.Count; i++)
            {
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    var u = nodes[i];
                    var v = nodes[j];
                    double dx = u.X - v.X;
                    double dy = u.Y - v.Y;
                    double dist = Math.Max(Math.Sqrt(dx * dx + dy * dy), 0.01);
                    double force = RepulsionK / (dist * dist);
                    double fx = dx / dist * force;
                    double fy = dy / dist * force;
                    u.Vx += fx;
                    u.Vy += fy;
                    v.Vx -= fx;
                    v.Vy -= fy;
                }
            }

            // Attraction (linked pairs only)
            foreach (var link in links)
            {
                if (!nodeDict.TryGetValue(link.SourceId, out var u) ||
                    !nodeDict.TryGetValue(link.TargetId, out var v))
                    continue;

                double dx = u.X - v.X;
                double dy = u.Y - v.Y;
                double dist = Math.Max(Math.Sqrt(dx * dx + dy * dy), 0.01);
                double force = AttractionK * dist * dist;
                double fx = dx / dist * force;
                double fy = dy / dist * force;
                u.Vx -= fx;
                u.Vy -= fy;
                v.Vx += fx;
                v.Vy += fy;
            }

            // Apply velocities with damping and temperature
            double maxDisplacement = 0;
            foreach (var node in nodes)
            {
                double vx = Math.Clamp(node.Vx * Damping, -temperature, temperature);
                double vy = Math.Clamp(node.Vy * Damping, -temperature, temperature);
                node.X = Math.Clamp(node.X + vx, 60, width - 60);
                node.Y = Math.Clamp(node.Y + vy, 40, height - 40);
                double disp = Math.Sqrt(vx * vx + vy * vy);
                if (disp > maxDisplacement) maxDisplacement = disp;
            }

            temperature *= 0.95;

            if (maxDisplacement < ConvergenceThreshold)
                break;
        }
    }
}

public class NodeInfo
{
    public string Id { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Vx { get; set; }
    public double Vy { get; set; }
}

public class LinkInfo
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
}
