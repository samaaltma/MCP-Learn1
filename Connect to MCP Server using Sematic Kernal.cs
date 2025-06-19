// Import packages
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;

public class Program
{
    public static async Task Main(string[] args)
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        // Read values from configuration
        var modelId = configuration["OpenAI:ModelId"] ?? string.Empty;
        var endpoint = configuration["OpenAI:Endpoint"] ?? string.Empty;
        var apiKey = configuration["OpenAI:ApiKey"] ?? string.Empty;
        var McpServerPath = configuration["McpServer:Path"] ?? string.Empty;

        // Build the kernel
        var builder = Kernel.CreateBuilder();
        builder.AddAzureOpenAIChatCompletion(modelId, endpoint, apiKey);
        builder.Services.AddLogging(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Trace));
        var kernel = builder.Build();
        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

        // MCP client setup
        try
        {
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "MyLocalMCPServer",
                Command = McpServerPath,
                Arguments = Array.Empty<string>()
            });

            var mcpClient = await McpClientFactory.CreateAsync(transport);
            var tools = await mcpClient.ListToolsAsync();

#pragma warning disable SKEXP0001 // Dereference of a possibly null reference
            var kernelFunctions = tools
                .Select(tool => tool.AsKernelFunction())
                .ToList();

            kernel.Plugins.AddFromFunctions("MyLocalMCPServer", kernelFunctions);

            Console.WriteLine($"✅ MCP initialized and {tools.Count()} tools loaded.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ MCP initialization failed: {ex.Message}");
        }

        // Execution settings with auto tool invocation
        var openAIPromptExecutionSettings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        // Chat loop
        var history = new ChatHistory();
        string? userInput;

        do
        {
            Console.Write("User > ");
            userInput = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(userInput)) continue;

            history.AddUserMessage(userInput);

            var result = await chatCompletionService.GetChatMessageContentAsync(
                history,
                executionSettings: openAIPromptExecutionSettings,
                kernel: kernel
            );

            Console.WriteLine("Assistant > " + result.Content);
            history.AddMessage(result.Role, result.Content ?? string.Empty);

        } while (userInput is not null);
    }
}
