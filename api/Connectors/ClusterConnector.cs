using k8s;
using k8s.Autorest;
using k8s.Models;

namespace api.Connectors
{
    public class ClusterConnector(IKubernetes client, ILogger<ClusterConnector> logger)
    {
        public async Task<IReadOnlyList<NodeSummary>> GetNodesAsync(CancellationToken ct)
        {

            var nodes = await client.CoreV1.ListNodeAsync(cancellationToken: ct);

            NodeMetricsList? metrics = null;
            try
            {
                metrics = await client.GetKubernetesNodesMetricsAsync();
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Node metrics unavailable; returning nodes without usage");
            }
            catch (HttpOperationException ex)
            {
                logger.LogWarning(ex, "Node metrics unavailable; returning nodes without usage");
            }

            var usageByNode = metrics?.Items.ToDictionary(m => m.Metadata.Name) ?? [];
            var nodeSummaries = nodes.Items.Select(node =>
            {
                usageByNode.TryGetValue(node.Metadata.Name, out var usage);
                return ToSummary(node, usage);
            }).ToList();

            return nodeSummaries;
        }

        public static NodeSummary ToSummary(V1Node node, NodeMetrics? usage)
        {
            return new NodeSummary(
                Name: node.Metadata.Name,
                Status: node.Status.Conditions?.FirstOrDefault(c => c.Type == "Ready")?.Status switch
                {
                    "True" => "Ready",
                    "False" => "NotReady",
                    _ => "Unknown"
                },
                CpuCores: node.Status.Capacity["cpu"].ToDecimal(),
                CpuUsedCores: usage?.Usage["cpu"].ToDecimal(),
                MemoryBytes: node.Status.Capacity["memory"].ToDecimal(),
                MemoryUsedBytes: usage?.Usage["memory"].ToDecimal()
            );
        }
    }

    public record NodeSummary(
        string Name,
        string Status,
        decimal CpuCores,
        decimal? CpuUsedCores,
        decimal MemoryBytes,
        decimal? MemoryUsedBytes);
}
