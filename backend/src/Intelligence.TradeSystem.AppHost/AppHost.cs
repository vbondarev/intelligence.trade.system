public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        builder
            .AddProject<Projects.Intelligence_TradeSystem_Api>("api")
            .WithExternalHttpEndpoints();

        builder.Build().Run();
    }
}
