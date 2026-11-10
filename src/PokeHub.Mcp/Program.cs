using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using PokeHub.Mcp;

// Servidor MCP do PokHub: expõe as operações do Core (projetos, versões, dados do jogo, ferramentas)
// para agentes de IA via stdio. Nada de rede: o agente roda este executável como processo filho.
//
// Uso: PokeHub.Mcp [--root <pasta do hub>]
// Sem --root usa POKEHUB_ROOT ou a pasta configurada pelo aplicativo (%AppData%\PokeHub\settings.json).

var builder = Host.CreateApplicationBuilder(args);

// stdout é o canal do protocolo; todo log vai para stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSingleton(_ => HubContext.Create(args));
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    // Erros das ferramentas voltam com a mensagem real (ex.: "projeto não existe"), que é o que o agente precisa para corrigir a chamada.
    .WithRequestFilters(f => f.AddCallToolFilter(next => async (ctx, ct) =>
    {
        try { return await next(ctx, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"[tool error] {ctx.Params?.Name}: {ex}");
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = ex.GetBaseException().Message }] };
        }
    }));

await builder.Build().RunAsync();
