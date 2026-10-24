using api.Connectors;
using k8s;
using k8s.Autorest;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IKubernetes>(_ =>
    {
        var kubeConfig = KubernetesClientConfiguration.BuildDefaultConfig();
        return new Kubernetes(kubeConfig);
    }
);
builder.Services.AddSingleton<ClusterConnector>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.MapGet("/api/health", () => Results.Ok(new { status = "OK" }));
app.MapGet("/api/cluster/nodes", async (ClusterConnector cluster, ILogger<Program> logger, CancellationToken ct) =>
{
    IReadOnlyList<NodeSummary> nodeSummaries;
    try
    {
        nodeSummaries = await cluster.GetNodesAsync(ct);
    }
    catch (HttpRequestException ex)
    {
        logger.LogError(ex, "Error retrieving node summaries");
        
        return Results.Problem(
            detail: "An error occurred while retrieving node summaries.",
            statusCode: StatusCodes.Status502BadGateway,
            title: "Node Cluster Unreachable"
        );
    }
    catch (HttpOperationException ex)
    {
        var statusCode = ex.Response.StatusCode == HttpStatusCode.Unauthorized ? StatusCodes.Status401Unauthorized : ex.Response.StatusCode == HttpStatusCode.Forbidden ? StatusCodes.Status403Forbidden : StatusCodes.Status502BadGateway;
        var detail = statusCode == StatusCodes.Status401Unauthorized ? "Unauthorized access to the Kubernetes cluster. If you are the owner, check RBAC." : statusCode == StatusCodes.Status403Forbidden ? "Forbidden access to the Kubernetes cluster. If you are the owner, check RBAC." : "An error occurred while retrieving node summaries.";
        logger.LogError(statusCode, detail);
        return Results.Problem(
            detail: "There was an error reaching the node cluster.",
            statusCode: StatusCodes.Status502BadGateway,
            title: "Node Cluster Unreachable"
        );
    }
    return Results.Ok(nodeSummaries);
});

app.Run();
