using api.Connectors;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace api.Tests
{
    public class ClusterConnectorTests
    {
        [Theory]
        [InlineData("True", "Ready")]
        [InlineData("False", "NotReady")]
        [InlineData("Unknown", "Unknown")]
        public void ToSummary_MapsReadyCondition(string conditionStatus, string expected)
        {
            var node = GetTestNode(conditionStatus);

            var summary = ClusterConnector.ToSummary(node, usage: null);

            Assert.Equal(expected, summary.Status);
        }

        [Fact(Explicit = true)]
        private static async Task GetNodesTest()
        {
            var client = new k8s.Kubernetes(k8s.KubernetesClientConfiguration.BuildDefaultConfig());
            ILogger<ClusterConnector> logger = new LoggerFactory().CreateLogger<ClusterConnector>();
            var connector = new ClusterConnector(client, logger);
            Assert.NotEmpty(await connector.GetNodesAsync(TestContext.Current.CancellationToken));
        }

        public static V1Node GetTestNode(string conditionStatus)
        {
            return new V1Node
            {
                Metadata = new V1ObjectMeta { Name = "rp1" },
                Status = new V1NodeStatus
                {
                    Conditions = [new V1NodeCondition { Type = "Ready", Status = conditionStatus }],
                    Capacity = new Dictionary<string, ResourceQuantity>
                    {
                        ["cpu"] = new ResourceQuantity("4"),
                        ["memory"] = new ResourceQuantity("4Gi"),
                    },
                },
            };
        }
    }
}
